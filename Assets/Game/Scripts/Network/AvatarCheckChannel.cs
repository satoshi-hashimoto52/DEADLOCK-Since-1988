using Unity.Netcode;

namespace Deadlock.Network
{
    /// <summary>
    /// Message channel for the automated two-process / two-device check (<see cref="NetworkCheckRunner"/>): the host
    /// sends a command to the owner of this avatar and gets a reply. Does nothing unless a check runner is active on
    /// the receiving side (started only with the -netCheck launch argument).
    /// </summary>
    public sealed class AvatarCheckChannel : NetworkBehaviour
    {
        [Rpc(SendTo.Owner)]
        public void CommandRpc(int id, string command)
        {
            if (NetworkCheckRunner.Instance != null)
                NetworkCheckRunner.Instance.OnCommand(this, id, command);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void ReplyRpc(int id, string reply)
        {
            if (NetworkCheckRunner.Instance != null)
                NetworkCheckRunner.Instance.OnReply(id, reply);
        }
    }
}
