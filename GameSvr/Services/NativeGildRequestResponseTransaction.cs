namespace GameSvr
{
    // Dormant model of the observable dispatch contract for Gild request-response ops 4611
    // (accept_request) and 4572 (refuse_request). Hex-Rays verified. Fail-closed in C#.
    //
    // These ops operate on a pending request object whose class carries a TYPE (1 = join request,
    // 2 = union/alliance request) and its own accept/refuse virtual methods. The server dispatches
    // through several layers before reaching the type-specific method:
    //
    //   4611 handler sub_6F62F0 -> role strategy [+0x00] sub_7039A0 @0x007039A0:
    //     req = sub_6A5284(); if !req -> 10;
    //     if req.type()==2 (union)  -> req.[vtbl+0x14] (union accept subtype);
    //     else (join) -> sub_704930 @0x00704930: req2 = sub_6A5284(); if !req2 -> 10;
    //                    if req2.type()==1 (join) -> req2.[vtbl+0x14] (join accept subtype, e.g.
    //                                                 555/12/13/5/6/1000/0); else -> sub_701D40.
    //   4572 handler sub_6F6340 -> role strategy [+0x04] sub_70443C @0x0070443C: mirror of the above
    //     with refuse subtype [vtbl+0x18], join fallback sub_704E54 -> sub_7029B4.
    //
    // Both reply via player.[vtbl+0x254] (buffered SendDefMessage carrying the request body). The
    // top-level contract (request-not-found -> 10; else dispatch by request type) is deterministic
    // and modeled exactly; the type-specific accept/refuse codes live in the request-object subtype
    // vtables (a further multi-class reverse) and are abstract inputs here.

    public enum NativeGildRequestResponseOp
    {
        AcceptRequest = 4611,
        RefuseRequest = 4572,
    }

    public enum NativeGildRequestType
    {
        None = 0,     // sub_6A5284 returned null
        Join = 1,     // req.type() == 1
        Union = 2,    // req.type() == 2
        Other = 3,    // neither 1 nor 2 (join fallback sub_701D40 / sub_7029B4)
    }

    public sealed class NativeGildRequestResponseContext
    {
        /// <summary>Pending request resolved (sub_6A5284 != null).</summary>
        public bool RequestFound { get; init; }
        /// <summary>Request object type discriminator (req.[vtbl+0x00]()).</summary>
        public NativeGildRequestType Type { get; init; }
        /// <summary>The type-specific accept/refuse subtype method result (polymorphic; abstract input).</summary>
        public int SubtypeResult { get; init; }
    }

    public static class NativeGildRequestResponseTransaction
    {
        public const int VtblAccept = 0x00;       // role strategy slot for 4611
        public const int VtblRefuse = 0x04;       // role strategy slot for 4572
        public const int VtblTypeDiscriminator = 0x00; // request object: type()
        public const int VtblSubtypeAccept = 0x14;     // request object: accept subtype method
        public const int VtblSubtypeRefuse = 0x18;     // request object: refuse subtype method
        public const int VtblSendBuffer = 0x254;       // buffered SendDefMessage

        public const int RequestNotFound = 10;

        /// <summary>
        /// Observable result. Both 4611 and 4572 share the same top-level shape (request-not-found ->
        /// 10; otherwise the request type selects the subtype accept/refuse method, whose code is
        /// forwarded verbatim). <paramref name="op"/> only selects which subtype vtable slot the
        /// original uses (accept +0x14 vs refuse +0x18); it does not change the observable ladder.
        /// </summary>
        public static int Evaluate(NativeGildRequestResponseOp op, NativeGildRequestResponseContext c)
        {
            if (!c.RequestFound || c.Type == NativeGildRequestType.None)
                return RequestNotFound;
            // Join / Union / Other all delegate to a (polymorphic) subtype method; its result is forwarded.
            return c.SubtypeResult;
        }

        /// <summary>The request-object vtable slot the original invokes for this op and request type.</summary>
        public static int SubtypeSlot(NativeGildRequestResponseOp op) =>
            op == NativeGildRequestResponseOp.AcceptRequest ? VtblSubtypeAccept : VtblSubtypeRefuse;
    }
}
