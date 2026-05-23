using System;
using System.Collections.Generic;
using System.Text;
using TerraFX.Interop.Windows;

namespace ECommons.IPC.Subscribers.CashFlow;

public unsafe class GilRecordSqlDescriptor
{
    public long GilPlayer { get; set; }
    public long GilRetainer { get; set; }
    public long UnixTime { get; set; }
    public ulong CidUlong { get; set; }
}
