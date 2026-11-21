using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.DTOs;
using WebApplication1.Models;
using WebApplication1.Repositories;

namespace WebApplication1.Services
{
    public class OrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly OrderRepository _orderRepository;
        private readonly CartRepository _cartRepository;
        private readonly ProductRepository _productRepository;
        private readonly UserRepository _userRepository;

        public OrderService(
            ApplicationDbContext context,
            OrderRepository orderRepository,
            CartRepository cartRepository,
            ProductRepository productRepository,
            UserRepository userRepository)
        {
            _context = context;
            _orderRepository = orderRepository;
            _cartRepository = cartRepository;
            _productRepository = productRepository;
            _userRepository = userRepository;
        }

        public async Task<IEnumerable<OrderDTO>> GetAllAsync(int? userId = null, string status = null)
        {
            var orders = await _orderRepository.GetAllAsync(userId, status);
            return orders.Select(MapToDTO);
        }

        public async Task<OrderDTO> GetByIdAsync(int id)
        {
            var order = await _orderRepository.GetByIdAsync(id);
            if (order == null)
                return null;

            return MapToDTO(order);
        }

        public async Task<IEnumerable<OrderDTO>> GetUserOrdersAsync(int userId)
        {
            var orders = await _orderRepository.GetAllAsync(userId);
            return orders.Select(MapToDTO);
        }

        /// <summary>
        /// Kept for callers that only need the order; returns null when checkout fails.
        /// Use <see cref="CheckoutAsync"/> to get the reason.
        /// </summary>
        public async Task<OrderDTO> CreateFromCartAsync(int userId)
        {
            return (await CheckoutAsync(userId)).Order;
        }

        /// <summary>
        /// Turns the user's cart into an order. Balance debit, stock decrement, order
        /// creation and cart clearing happen in one serializable transaction: either
        /// everything is saved or nothing is. Validation (stock and balance) runs inside
        /// the transaction, on freshly read data.
        /// </summary>
        public async Task<CheckoutResult> CheckoutAsync(int userId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                    return await RollBack(transaction, "Utilizador não encontrado.");

                var cartItems = (await _cartRepository.GetUserCartAsync(userId))?.ToList();
                if (cartItems == null || cartItems.Count == 0)
                    return await RollBack(transaction, "O carrinho está vazio.");

                // Stock: nunca vender mais do que existe, e o stock nunca fica negativo
                foreach (var item in cartItems)
                {
                    if (item.Quantity <= 0)
                        return await RollBack(transaction, $"Quantidade inválida para \"{item.Product.Name}\".");

                    if (item.Quantity > item.Product.Stock)
                        return await RollBack(transaction,
                            $"Stock insuficiente para \"{item.Product.Name}\": pediste {item.Quantity}, há {item.Product.Stock}.");
                }

                decimal totalAmount = cartItems.Sum(item => item.Product.Price * item.Quantity);

                if (user.Balance < totalAmount)
                    return await RollBack(transaction,
                        $"Saldo insuficiente: o total é €{totalAmount:F2} e o saldo é €{user.Balance:F2}.");

                user.Balance -= totalAmount;
                await _userRepository.UpdateAsync(user);

                var order = new Order
                {
                    UserId = userId,
                    OrderDate = DateTime.Now,
                    TotalAmount = totalAmount,
                    Status = "Pending",
                    Items = new List<OrderItem>()
                };

                foreach (var item in cartItems)
                {
                    order.Items.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.Product.Price
                    });

                    item.Product.Stock -= item.Quantity;
                    await _productRepository.UpdateAsync(item.Product);
                }

                await _orderRepository.AddAsync(order);
                await _cartRepository.ClearCartAsync(userId);

                await transaction.CommitAsync();

                return CheckoutResult.Success(await GetByIdAsync(order.Id));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Checkout failed and was rolled back: {ex.Message}");
                await transaction.RollbackAsync();
                _context.ChangeTracker.Clear();
                return CheckoutResult.Failure("Não foi possível concluir a compra. Nada foi cobrado; tenta novamente.");
            }
        }

        private static async Task<CheckoutResult> RollBack(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string error)
        {
            await transaction.RollbackAsync();
            return CheckoutResult.Failure(error);
        }

        public async Task<bool> UpdateStatusAsync(int id, string status, int? staffId = null)
        {
            Console.WriteLine($"[API] UpdateStatusAsync - OrderId: {id}, Status: {status}, StaffId: {staffId}");
            
            var order = await _orderRepository.GetByIdAsync(id);
            if (order == null)
            {
                Console.WriteLine($"[API] Order {id} not found");
                return false;
            }

            Console.WriteLine($"[API] Order found - Current Status: {order.Status}, UserId: {order.UserId}, TotalAmount: {order.TotalAmount}");

            // Validar o status
            if (!IsValidStatus(status))
            {
                Console.WriteLine($"[API] Invalid status: {status}");
                return false;
            }

            // Validar transição de estado
            if (!IsValidTransition(order.Status, status))
            {
                Console.WriteLine($"[API] Invalid transition from {order.Status} to {status}");
                return false;
            }

            // Se estiver finalizando a entrega, verificar se o funcionário existe
            if (status == "Delivered" && !staffId.HasValue)
            {
                Console.WriteLine($"[API] Delivered status requires staffId");
                return false;
            }

            // Se estiver cancelando, devolver o saldo ao usuário
            if (status == "Canceled" && order.Status == "Pending")
            {
                Console.WriteLine($"[API] Canceling order - refunding balance to user {order.UserId}");
                var user = await _userRepository.GetByIdAsync(order.UserId);
                if (user != null)
                {
                    var oldBalance = user.Balance;
                    user.Balance += order.TotalAmount;
                    Console.WriteLine($"[API] User balance: {oldBalance} -> {user.Balance}");
                    await _userRepository.UpdateAsync(user);
                    Console.WriteLine($"[API] User balance updated successfully");
                }
                else
                {
                    Console.WriteLine($"[API] User {order.UserId} not found!");
                }
            }

            // Atualizar o pedido
            order.Status = status;
            
            if (status == "Delivered")
            {
                order.DeliveredAt = DateTime.Now;
                order.DeliveredByStaffId = staffId;
            }
            else if (status == "Canceled")
            {
                order.DeliveredAt = DateTime.Now;
            }

            await _orderRepository.UpdateAsync(order);
            return true;
        }

        private bool IsValidStatus(string status)
        {
            return status == "Pending" || 
                   status == "Ready" || 
                   status == "Delivered" ||
                   status == "Canceled";
        }

        private bool IsValidTransition(string currentStatus, string newStatus)
        {
            // Transições válidas:
            // Pending -> Ready (aprovar pedido)
            // Pending -> Canceled (cancelar pedido)
            // Ready -> Delivered (entregar pedido)
            // Ready -> Canceled (cancelar após aprovação - excepcional)
            
            return (currentStatus, newStatus) switch
            {
                ("Pending", "Ready") => true,
                ("Pending", "Canceled") => true,
                ("Ready", "Delivered") => true,
                ("Ready", "Canceled") => true,
                _ => false
            };
        }

        private OrderDTO MapToDTO(Order order)
        {
            return new OrderDTO
            {
                Id = order.Id,
                UserId = order.UserId,
                UserName = order.User?.Name,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                DeliveredAt = order.DeliveredAt,
                Items = order.Items?.Select(item => new OrderItemDTO
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.Product?.Name,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = item.UnitPrice * item.Quantity
                }).ToList() ?? new List<OrderItemDTO>()
            };
        }
    }
}
