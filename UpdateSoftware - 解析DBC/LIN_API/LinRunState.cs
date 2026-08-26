using System;
using System.Collections.Generic;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// 报文级运行状态（方案 4.2 状态机）。配置对象 <see cref="LinTransmitEntry"/> 只保存用户配置；
    /// 运行时快照按 (逻辑通道, 裸 PID) 索引，携带序列号/代数并区分发送意图、驱动提交与真实总线结果。
    /// </summary>
    public enum LinRunStateKind
    {
        /// <summary>未启用：不在发送/响应计划中</summary>
        Disabled = 0,
        /// <summary>已启用并进入计划（Master/HeaderOn 等待调度；Slave 已武装）</summary>
        Armed = 1,
        /// <summary>已提交到驱动队列（api 返回 errOK，不等于总线上已出现完整帧）</summary>
        TxSubmitted = 2,
        /// <summary>HeaderOn：Header 已发，等待真实远端响应</summary>
        WaitingResponse = 3,
        /// <summary>Slave：已武装响应，等待外部 Master Header 触发</summary>
        WaitingExternalHeader = 4,
        /// <summary>真实总线结果完成（Master 完整帧 / HeaderOn 收到真实响应 / Slave 收到外部 Header 并响应）</summary>
        Completed = 5,
        /// <summary>无应答：HeaderOn 响应窗口到期且无真实 Rx（仅此状态点亮无应答红灯）</summary>
        NoResponse = 6,
        /// <summary>校验和/同步/硬件错误（真实错误标志驱动）</summary>
        Error = 7,
        /// <summary>该模式当前适配器明确不支持（Break Only），不伪装成功</summary>
        Unsupported = 8,
    }

    /// <summary>
    /// 发送/运行状态事件载荷（计划阶段 2：发送意图、驱动提交、真实总线帧、响应完成/超时）。
    /// 事件携带序列号与裸 PID；重复周期同 PID 的多次请求用 Generation/有序 pending 区分，不互相覆盖。
    /// </summary>
    public enum LinTxEventKind
    {
        /// <summary>已加入发送/响应计划（启用时发出）</summary>
        Intent = 0,
        /// <summary>驱动提交成功（errOK，仅代表进入驱动队列）</summary>
        SubmitOk = 1,
        /// <summary>驱动提交失败</summary>
        SubmitFail = 2,
        /// <summary>真实总线帧（仅 Direction=Rx 的硬件帧；Tx 提交回显不是真实总线帧）</summary>
        BusFrame = 3,
        /// <summary>HeaderOn：进入等待应答</summary>
        ResponseWait = 4,
        /// <summary>真实 Rx 完成响应</summary>
        ResponseComplete = 5,
        /// <summary>响应窗口到期且无真实 Rx：无应答</summary>
        ResponseTimeout = 6,
        /// <summary>校验和/同步/硬件错误帧（真实错误标志）</summary>
        Error = 7,
        /// <summary>该模式不被适配器支持（Break Only）</summary>
        Unsupported = 8,
        /// <summary>报文已停用（停止新的发送/响应）</summary>
        Disabled = 9,
    }

    /// <summary>发送状态事件（阶段 3 UI 订阅；全部使用裸 ID）</summary>
    public sealed class LinTxEvent
    {
        public byte LogicChannel;
        /// <summary>裸 6-bit ID</summary>
        public byte Pid;
        public LinTransmitType Type;
        public LinTxEventKind Kind;
        public string Text = "";
        public override string ToString()
        {
            return "ch" + LogicChannel + " pid=0x" + Pid.ToString("X2") + " type=" + Type + " " + Kind + (Text.Length > 0 ? " " + Text : "");
        }
    }

    /// <summary>
    /// 运行快照（不可序列化；按 (LogicChannel, Pid) 索引，Lin_API 维护）。
    /// 状态机：
    /// Disabled →(启用) Armed →(Master/HeaderOn 调度提交) TxSubmitted →(HeaderOn) WaitingResponse
    /// WaitingResponse →(真实 Rx) Completed / →(deadline) NoResponse；Master →Completed（本机完整帧）
    /// Slave →WaitingExternalHeader（等外部 Header，不报错）；任何状态 →(校验/同步/硬件) Error
    /// 任何状态 →(停用) Disabled
    /// </summary>
    public sealed class LinRunSnapshot
    {
        public byte LogicChannel;
        /// <summary>裸 6-bit ID</summary>
        public byte Pid;
        public LinTransmitType Type;
        public bool Enabled;
        /// <summary>启用代数：每次停用再启用 +1；Generation 与裸 PID 一起唯一标识一次发送周期</summary>
        public int Generation;
        /// <summary>最近发送意图时刻（微秒，会话归零）</summary>
        public long LastIntentUs;
        /// <summary>最近真实总线帧时刻（仅 Direction=Rx 的硬件帧；Tx 提交回显不计入）</summary>
        public long LastBusUs;
        /// <summary>HeaderOn 响应截止时刻（微秒；0=当前不等待）</summary>
        public long ExpectedResponseDeadlineUs;
        /// <summary>驱动提交成功次数</summary>
        public int SubmittedCount;
        /// <summary>真实总线帧次数（仅 Direction=Rx 的硬件帧；Tx 提交回显不计入）</summary>
        public int BusFrameCount;
        /// <summary>错误次数（NoResponse/校验/同步/硬件/不支持）</summary>
        public int ErrorCount;
        /// <summary>实测周期（ms）：相同方向相同裸 PID 的相邻真实总线帧间隔；0=样本不足（UI 显示 --）</summary>
        public long MeasuredPeriodMs;
        public LinRunStateKind State = LinRunStateKind.Disabled;
        public LinErrorKind ErrorKind = LinErrorKind.None;
        public string ErrorText = "";

        public bool IsError
        {
            get
            {
                return State == LinRunStateKind.NoResponse || State == LinRunStateKind.Error || State == LinRunStateKind.Unsupported;
            }
        }
    }
}