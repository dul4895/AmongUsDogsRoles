using Hazel;
using MiraAPI.Utilities;
using Reactor.Networking.Attributes;
using Reactor.Networking.Rpc;

namespace AmongUsDogsRoles;

public enum Ability : byte { Kill, Drag, Release, Execute, Detonate, Investigate, Mark, Recall, Alert, Shoot, Examine, Fake, Unfake }
public readonly record struct Request(Ability Ability, byte Target);
public enum StateKind : byte { Drag, Release, Alert, Mark, ClearMark, Track, Reveal, Cooldown, Blast, Fake, Unfake }
public readonly record struct Update(StateKind Kind, byte Actor, byte Target = 255, float Value = 0, float X = 0, float Y = 0);

// Reactor binds the request to the sender's PlayerControl. Clients never supply a killer identity.
[RegisterCustomRpc(1)]
public sealed class AbilityRpc(Plugin plugin, uint id) : PlayerCustomRpc<Plugin, Request>(plugin, id)
{
    public override RpcLocalHandling LocalHandling => RpcLocalHandling.Before;
    public override void Write(MessageWriter w, Request d) { w.Write((byte)d.Ability); w.Write(d.Target); }
    public override Request Read(MessageReader r) => new((Ability)r.ReadByte(), r.ReadByte());
    public override void Handle(PlayerControl sender, Request d)
    {
        if (AmongUsClient.Instance.AmHost) AbilityService.Handle(sender, d);
    }
    public static void Request(Ability ability, byte target = 255) =>
        Rpc<AbilityRpc>.Instance.SendTo(AmongUsClient.Instance.HostId, new(ability, target));
}

[RegisterCustomRpc(2)]
public sealed class StateRpc(Plugin plugin, uint id) : PlayerCustomRpc<Plugin, Update>(plugin, id)
{
    public override RpcLocalHandling LocalHandling => RpcLocalHandling.Before;
    public override void Write(MessageWriter w, Update d)
    { w.Write((byte)d.Kind); w.Write(d.Actor); w.Write(d.Target); w.Write(d.Value); w.Write(d.X); w.Write(d.Y); }
    public override Update Read(MessageReader r) => new((StateKind)r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public override void Handle(PlayerControl sender, Update d)
    {
        if (sender.IsHost()) RoundState.Apply(d);
    }
    public static void Broadcast(Update update) => Rpc<StateRpc>.Instance.Send(update, true);
    public static void Private(PlayerControl player, Update update)
    {
        // SendTo also handles locally, and a server can echo a self-addressed RPC.
        // A private result for the host needs no network trip or second notification.
        if (player.AmOwner) RoundState.Apply(update);
        else Rpc<StateRpc>.Instance.SendTo(player.OwnerId, update);
    }
}
