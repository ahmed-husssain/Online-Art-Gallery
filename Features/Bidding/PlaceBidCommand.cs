using MediatR;

namespace Project.Features.Bidding
{
    public record BidResult(
        bool Success,
        string? Message = null,
        decimal? NewBid = null,
        int? NewCount = null,
        string? RedirectUrl = null
    );
    public record PlaceBidCommand(
        int ProductId,
        decimal Amount,
        int UserId,
        string IdempotencyKey
    ): IRequest<BidResult>;
}