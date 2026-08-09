using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// In-memory model of one player's active STALL (摆摊) booth — the executable record the write handlers
    /// resolve via the owner map (task #83, wave-1). Field set is reversed from the record descriptor
    /// (<see cref="NativeStallManagerModel"/>, ctor <c>sub_61ED04</c>) + the <c>gamedata.stall</c> columns
    /// (<see cref="NativeStallHeaderRow"/>). This is a plain data model — no lookup / mutation / persistence
    /// lives here; the manager + executors (still gated on the wave-2 idat) own those.
    ///
    /// Native record offsets (see NativeStallManagerModel): owner name key +0x08/+0x0C, DB idx +0x18,
    /// createdate +0x20, modifydate +0x28, level +0x30, status +0x40 (0 initial / 1 running / 2 paused),
    /// items TList +0x3C, orders TList +0x54.
    /// </summary>
    public sealed class NativeStallRecord
    {
        /// <summary><c>stall.idx</c> (0 until the INSERT returns LAST_INSERT_ID). Native rec +0x18.</summary>
        public int DbIdx { get; set; }

        /// <summary>Owner char id (bigint). Ties the booth + its items/orders to the seller.</summary>
        public long OwnerId { get; set; }

        /// <summary>Owner char name — the manager lookup key (native rec +0x08/+0x0C, primed by sub_40C988).</summary>
        public string OwnerName { get; set; } = string.Empty;

        public string StallName { get; set; } = string.Empty;

        /// <summary>Booth level (native rec +0x30; default 1 at ctor).</summary>
        public int Level { get; set; } = 1;

        /// <summary>Booth TTL in hours (the expire tick closes booths older than DuraTime).</summary>
        public int DuraTime { get; set; }

        /// <summary><c>stall.isEnabled</c> — 1 while live, 0 once expired/closed.</summary>
        public int IsEnabled { get; set; }

        /// <summary>Native rec +0x40: 0 initial / 1 running / 2 paused-closed (the StallRunning gate).</summary>
        public StallRecordStatus Status { get; set; } = StallRecordStatus.Initial;

        /// <summary>Native rec +0x20 (set at ctor / when status==0).</summary>
        public DateTime CreateDate { get; set; }

        /// <summary>Native rec +0x28 (refreshed by sub_61FEAC on every persist).</summary>
        public DateTime ModifyDate { get; set; }

        public int PosX { get; set; } = -1;   // native rec +0x44 (-1 at ctor)
        public int PosY { get; set; } = -1;   // native rec +0x48 (-1 at ctor)
        public string MapName { get; set; } = string.Empty;

        /// <summary>Items on sale (native items TList rec +0x3C / per-record item hash rec +0x50).</summary>
        public List<NativeStallItem> Items { get; } = new();

        /// <summary>Buyer-order objects appended on purchase (native orders TList rec +0x54).</summary>
        public List<NativeStallBuyerOrder> Orders { get; } = new();

        /// <summary>Header row for the store's INSERT/UPDATE (maps this record to <c>gamedata.stall</c>).</summary>
        public NativeStallHeaderRow ToHeaderRow() => new()
        {
            OwnerId = OwnerId,
            OwnerName = OwnerName,
            StallName = StallName,
            ItemCnt = Items.Count,
            Level = Level,
            DuraTime = DuraTime,
            IsEnabled = IsEnabled,
            CreateDate = CreateDate,
            ModifyDate = ModifyDate,
            PosX = PosX,
            PosY = PosY,
            MapName = MapName,
            Status = (int)Status,
        };
    }

    /// <summary>
    /// One item listed on a booth — the 208-byte <c>srvData</c> item plus the <c>gamedata.stallitem</c>
    /// pricing/flag columns. The item body (de)serializes via <see cref="NativeStallItemRecordCodec"/>.
    /// </summary>
    public sealed class NativeStallItem
    {
        /// <summary><c>stallitem.idx</c> (0 until INSERT returns LAST_INSERT_ID).</summary>
        public int DbIdx { get; set; }

        /// <summary>The item on sale (serialized to the 208-byte <c>srvData</c> BLOB).</summary>
        public TUserItem Item { get; set; }

        public int UnitPrice { get; set; }   // stallitem.uprice (native stallItem[+0xF8])
        public int MoneyType { get; set; }    // stallitem.moneytype (native [+0xF4]): 0 gold / 1 balance
        public int ItemCount { get; set; }    // stallitem.itemcount (stackable qty)
        public bool IsSold { get; set; }      // stallitem.isSold
        public bool IsGetMoney { get; set; }  // stallitem.isGetMoney (seller collected)
        public bool IsBoSended { get; set; }  // stallitem.IsBoSended (buyer-order dispatched)
    }

    /// <summary>
    /// A buyer order — the crash-recovery ledger row (<c>gamedata.buyer_order</c>) + the 208-byte item body.
    /// Native in-memory object sub_620F58 (264 bytes; item struct at +0x1F).
    /// </summary>
    public sealed class NativeStallBuyerOrder
    {
        public int DbIdx { get; set; }        // buyer_order.idx
        public long BuyerId { get; set; }
        public string BuyerName { get; set; } = string.Empty;
        public long SellerId { get; set; }
        public string SellerName { get; set; } = string.Empty;
        public int UnitPrice { get; set; }
        public int MoneyType { get; set; }
        public int Count { get; set; }
        public int TotalPrice { get; set; }
        public int BoDecMoney { get; set; }   // buyer money already deducted (idempotent phase flag)
        public int Status { get; set; }
        public TUserItem Item { get; set; }   // buyer-order srvData item (native obj +0x1F)
    }
}
