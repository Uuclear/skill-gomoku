using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gomoku.Net
{
    /// <summary>
    /// Wire envelope. Field names mirror server/internal/proto/messages.go
    /// (the single source of truth). All JSON is camelCase.
    /// </summary>
    public sealed class Envelope
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("seq", NullValueHandling = NullValueHandling.Ignore)]
        public long? Seq;
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Data;
    }

    public struct Point
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
    }

    // ---------- Client -> server payloads ----------

    public sealed class LoginReq
    {
        [JsonProperty("nickname")] public string Nickname;
        [JsonProperty("deviceId")] public string DeviceId;
    }

    public sealed class SkillIdsReq
    {
        [JsonProperty("skillIds")] public List<int> SkillIds;
    }

    public sealed class RoomJoinReq
    {
        [JsonProperty("roomCode")] public string RoomCode;
        [JsonProperty("skillIds")] public List<int> SkillIds;
    }

    public sealed class MoveReq
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
    }

    public sealed class UseSkillReq
    {
        [JsonProperty("skillId")] public int SkillId;
        [JsonProperty("targets")] public List<Point> Targets;
        [JsonProperty("dir")] public int Dir; // 0:+x 1:+y 2:-x 3:-y
    }

    public sealed class RematchReq
    {
        [JsonProperty("accept")] public bool Accept;
    }

    public sealed class EmoteReq
    {
        [JsonProperty("id")] public int Id;
    }

    // ---------- Server -> client payloads ----------

    public sealed class LoginOkMsg
    {
        [JsonProperty("playerId")] public string PlayerId;
    }

    public sealed class ErrorMsg
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("seq")] public long? Seq;
    }

    public sealed class OpponentInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("nickname")] public string Nickname;
    }

    public sealed class MatchFoundMsg
    {
        [JsonProperty("roomId")] public string RoomId;
        [JsonProperty("color")] public int Color; // 0 black 1 white
        [JsonProperty("opponent")] public OpponentInfo Opponent;
        [JsonProperty("skillIds")] public List<int> SkillIds;
    }

    public sealed class RoomPlayerInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("nickname")] public string Nickname;
        [JsonProperty("ready")] public bool Ready;
        [JsonProperty("skillCnt")] public int SkillCnt;
        [JsonProperty("isHost")] public bool IsHost;
    }

    public sealed class RoomStateMsg
    {
        [JsonProperty("roomCode")] public string RoomCode;
        [JsonProperty("hostId")] public string HostId;
        [JsonProperty("players")] public List<RoomPlayerInfo> Players;
    }

    public sealed class GameConfig
    {
        [JsonProperty("startEnergy")] public int StartEnergy;
        [JsonProperty("gain")] public int Gain;
        [JsonProperty("maxEnergy")] public int MaxEnergy;
        [JsonProperty("turnSeconds")] public int TurnSeconds;
        [JsonProperty("boardSize")] public int BoardSize;
    }

    public sealed class GameStartMsg
    {
        [JsonProperty("roomId")] public string RoomId;
        [JsonProperty("boardSize")] public int BoardSize;
        [JsonProperty("yourColor")] public int YourColor;
        [JsonProperty("blackSkills")] public List<int> BlackSkills;
        [JsonProperty("whiteSkills")] public List<int> WhiteSkills;
        [JsonProperty("config")] public GameConfig Config;
    }

    public sealed class SkillStateDto
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("cd")] public int Cd;
        [JsonProperty("usesLeft")] public int UsesLeft; // -1 = unlimited
    }

    public sealed class SideStateDto
    {
        [JsonProperty("energy")] public int Energy;
        [JsonProperty("skills")] public List<SkillStateDto> Skills;
        [JsonProperty("silenceTurns")] public int SilenceTurns;
        [JsonProperty("noEnergyTurns")] public int NoEnergyTurns;
        [JsonProperty("discountNext")] public bool DiscountNext;
        [JsonProperty("focusTurns")] public int FocusTurns;
    }

    public sealed class StoneDto
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("c")] public int C; // 0 black 1 white
    }

    public sealed class PhantomDto
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("c")] public int C;
        [JsonProperty("expireAtMove")] public int ExpireAtMove;
    }

    public sealed class MarkPointDto
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("expireAtMove")] public int ExpireAtMove;
    }

    public sealed class ZoneDto
    {
        [JsonProperty("cx")] public int Cx;
        [JsonProperty("cy")] public int Cy;
        [JsonProperty("expireAtMove")] public int ExpireAtMove;
    }

    public sealed class CageDto
    {
        [JsonProperty("center")] public Point Center;
        [JsonProperty("expireAtMove")] public int ExpireAtMove;
    }

    public sealed class Snapshot
    {
        [JsonProperty("status")] public string Status; // playing/over
        [JsonProperty("turn")] public int Turn;       // 0 black 1 white
        [JsonProperty("moveCount")] public int MoveCount;
        [JsonProperty("turnEndsAtMs")] public long TurnEndsAtMs;
        [JsonProperty("stones")] public List<StoneDto> Stones;
        [JsonProperty("phantoms")] public List<PhantomDto> Phantoms;
        [JsonProperty("fortifies")] public List<MarkPointDto> Fortifies;
        [JsonProperty("shields")] public List<MarkPointDto> Shields;
        [JsonProperty("seals")] public List<MarkPointDto> Seals;
        [JsonProperty("zones")] public List<ZoneDto> Zones;
        [JsonProperty("cage")] public CageDto Cage;
        [JsonProperty("black")] public SideStateDto Black;
        [JsonProperty("white")] public SideStateDto White;
        // Only present in the own private view.
        [JsonProperty("minePositions")] public List<Point> MinePositions;
    }

    public sealed class GameEvent
    {
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("actor")] public int Actor;
        [JsonProperty("skillId")] public int? SkillId;
        [JsonProperty("x")] public int? X;
        [JsonProperty("y")] public int? Y;
        [JsonProperty("points")] public List<Point> Points;
        [JsonProperty("extraTurn")] public bool? ExtraTurn;
    }

    public sealed class GameEventMsg
    {
        [JsonProperty("event")] public GameEvent Event;
        [JsonProperty("state")] public Snapshot State;
    }

    public sealed class PrivateSkillMsg
    {
        [JsonProperty("skillId")] public int SkillId;
        [JsonProperty("payload")] public JObject Payload;
    }

    public sealed class OraclePayload
    {
        [JsonProperty("points")] public List<Point> Points;
    }

    public sealed class LineStats
    {
        [JsonProperty("longest")] public int Longest;
        [JsonProperty("open3")] public int Open3;
        [JsonProperty("open4")] public int Open4;
    }

    public sealed class AnalyzePayload
    {
        [JsonProperty("black")] public LineStats Black;
        [JsonProperty("white")] public LineStats White;
    }

    public sealed class GameOverMsg
    {
        [JsonProperty("winner")] public int? Winner; // null = draw
        [JsonProperty("reason")] public string Reason; // five/surrender/timeout/full/left
        [JsonProperty("winLine")] public List<Point> WinLine;
    }

    public sealed class EmoteMsg
    {
        [JsonProperty("fromColor")] public int FromColor;
        [JsonProperty("id")] public int Id;
    }

    public sealed class MatchWaitingMsg
    {
        [JsonProperty("waitSeconds")] public int WaitSeconds;
    }

    public sealed class RematchRequestedMsg
    {
        [JsonProperty("fromColor")] public int FromColor;
    }

    public sealed class OpponentLeftMsg
    {
        [JsonProperty("reason")] public string Reason; // left/disconnect
    }

    public sealed class TurnExpireWarnMsg
    {
        [JsonProperty("remainSeconds")] public int RemainSeconds;
    }
}
