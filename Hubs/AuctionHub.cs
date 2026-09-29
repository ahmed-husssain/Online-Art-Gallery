using Microsoft.AspNetCore.SignalR;

namespace Project.Hubs
{
    public class AuctionHub : Hub
    {
        public async Task  JoinAuctionGroup(int productId){
            await Groups.AddToGroupAsync(Context.ConnectionId,
            $"auction-{productId}");
        }
        public async Task LeaveAuctionGroup(int productId){
            await Groups.RemoveFromGroupAsync(Context.ConnectionId,
            $"auction-{productId}");
        }
    }
}