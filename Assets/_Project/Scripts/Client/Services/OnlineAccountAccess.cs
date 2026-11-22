using System.Threading.Tasks;
using PokeChess.Network.Session;
namespace PokeChess.Client.Services
{
    public static class OnlineAccountAccess
    {
        public static async Task<string> Admission(string room,string nonce)
        {
            var reply=await AccountSessionService.Instance.RequestOnlineAsync("/auth/ugs/admission",new AccountPayload { roomId=room,nonce=nonce });
            return reply.Status==200 && AccountValidation.Token(reply.AdmissionTicket)?reply.AdmissionTicket:null;
        }
        public static async Task<bool?> Verify(string room,string nonce,string player,string ticket)
        {
            var reply=await AccountSessionService.Instance.RequestOnlineAsync("/auth/ugs/admission/verify",new AccountPayload { roomId=room,nonce=nonce,playerId=player,admissionTicket=ticket });
            return reply.Status==200?(bool?)reply.Authorized:reply.Status==401 || reply.Status==403?(bool?)false:null;
        }
        public static async Task<bool> Host(string player)
        {
            var reply=await AccountSessionService.Instance.RequestOnlineAsync("/auth/ugs/player",new AccountPayload { playerId=player });
            return reply.Status==200 && reply.Authorized;
        }
    }
}
