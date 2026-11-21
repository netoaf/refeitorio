using WebApplication1.DTOs;

namespace WebApplication1.Services
{
    /// <summary>
    /// Outcome of a checkout: either the created order, or a clear error message.
    /// </summary>
    public class CheckoutResult
    {
        public OrderDTO Order { get; private init; }
        public string Error { get; private init; }
        public bool Succeeded => Order != null;

        public static CheckoutResult Success(OrderDTO order) => new() { Order = order };
        public static CheckoutResult Failure(string error) => new() { Error = error };
    }
}
