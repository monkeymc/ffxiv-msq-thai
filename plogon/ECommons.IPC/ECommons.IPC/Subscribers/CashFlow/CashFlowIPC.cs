using ECommons.ChatMethods;
using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Text;
using static ECommons.IPC.Subscribers.CashFlow.CashFlowIPC.Delegates;

namespace ECommons.IPC.Subscribers.CashFlow;

public class CashFlowIPC : IPCBase
{
    public CashFlowIPC()
    {
    }

    public CashFlowIPC(SafeWrapper wrapper) : base(wrapper)
    {
    }

    public override string InternalName { get; } = "CashFlow";

    public static class Delegates
    {
        public delegate List<GilRecordSqlDescriptor> GetGilRecords(long unixTimeMsMin, long unixTimeMsMax);
        public delegate Sender? GetPlayerInfo(ulong CID);
    }

    /// <summary>
    /// Use asynchronously
    /// </summary>
    [EzIPC(nameof(GetGilRecords))] public GetGilRecords GetGilRecords;

    /// <summary>
    /// Use asynchronously
    /// </summary>
    [EzIPC(nameof(GetPlayerInfo))] public GetPlayerInfo GetPlayerInfo;
}
