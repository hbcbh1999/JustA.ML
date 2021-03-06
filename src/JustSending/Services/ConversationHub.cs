using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Infrastructure;

namespace JustSending.Services
{
    public class ConversationHub : Hub
    {
        private readonly AppDbContext _db;
        private readonly IConnectionManager _connectionManager;
        private readonly IHostingEnvironment _env;

        public ConversationHub(AppDbContext db, IConnectionManager connectionManager, IHostingEnvironment env)
        {
            _db = db;
            _connectionManager = connectionManager;
            _env = env;
        }

        internal void RequestReloadMessage(string sessionId)
        {
            GetClients(sessionId).requestReloadMessage();
        }

        internal void ShowSharePanel(string sessionId, int token)
        {
            GetClients(sessionId).showSharePanel(token.ToString("### ###"));
        }

        internal void HideSharePanel(string sessionId)
        {
            GetClients(sessionId).hideSharePanel();
        }

        internal void SessionDeleted(string sessionId)
        {
            GetClients(sessionId).sessionDeleted();
        }

        internal void SendNumberOfDevices(string sessionId, int count)
        {
            GetClients(sessionId).setNumberOfDevices(count);
        }

        internal int SendNumberOfDevices(string sessionId)
        {
            var numConnectedDevices = _db.Connections.Count(x => x.SessionId == sessionId);
            SendNumberOfDevices(sessionId, numConnectedDevices);
            return numConnectedDevices;
        }

        private dynamic GetClients(string sessionId)
        {
            var hub = _connectionManager.GetHubContext<ConversationHub>();
            var connectionIds = _db.FindClient(sessionId);

            return hub.Clients.Clients(connectionIds.ToList());
        }

        private const string stringSessionKey = "session";
        public override Task OnConnected()
        {
            return Task.CompletedTask;
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            var sessionId = _db.UntrackClientReturnSessionId(Context.ConnectionId);
            if (!string.IsNullOrEmpty(sessionId))
            {
                var numDevices = SendNumberOfDevices(sessionId);
                if (numDevices == 0)
                {
                    EraseSessionInternal(sessionId);
                }
            }

            return Task.CompletedTask;
        }

        public void Connect(string sessionId)
        {
            _db.TrackClient(sessionId, Context.ConnectionId);

            // Check if any active share token is open
            //
            CheckIfShareTokenExists(sessionId);

            SendNumberOfDevices(sessionId, _db.Connections.Count(x => x.SessionId == sessionId));
        }

        private bool CheckIfShareTokenExists(string sessionId, bool notifyIfExist = true)
        {
            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == sessionId);
            if (shareToken != null)
            {
                if (notifyIfExist)
                    ShowSharePanel(sessionId, shareToken.Id);
                return true;
            }
            return false;
        }

        public void Share()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            // Check if any share exist
            if (!CheckIfShareTokenExists(connection.SessionId))
            {
                var token = _db.CreateNewShareToken(connection.SessionId);
                ShowSharePanel(connection.SessionId, token);
            }
        }

        public void CancelShare()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == connection.SessionId);
            if (shareToken != null)
            {
                _db.ShareTokens.Delete(shareToken.Id);
                HideSharePanel(connection.SessionId);
            }
        }

        public void EraseSession()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            EraseSessionInternal(connection.SessionId);
        }

        private void EraseSessionInternal(string sessionId)
        {
            _db.Sessions.Delete(sessionId);
            _db.Messages.Delete(x => x.SessionId == sessionId);
            _db.ShareTokens.Delete(x => x.SessionId == sessionId);

            SessionDeleted(sessionId);

            _db.Connections.Delete(x => x.SessionId == sessionId);

            try
            {
                var folder = Controllers.AppController.GetUploadFolder(sessionId, _env.WebRootPath);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            } catch {
                // ToDo: Schedule delete later
            }
        }

        public override Task OnReconnected()
        {
            return OnConnected();
        }
    }
}