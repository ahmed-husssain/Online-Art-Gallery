using MediatR;
using Project.Models;

namespace Project.Features.Checkout{
    public record CheckoutResult(
        bool Success,
        int? OrderId = null,
        string? Message = null
    );
    public record ProcessCheckoutCommand(
        int UserId,
        string? UserEmail,
        string? FullName,
        string? Address,
        string? City,
        string? ZipCode,
        List<CartItem> CartItems,
        string ConfirmLinkPattern
    ): IRequest<CheckoutResult>;
}