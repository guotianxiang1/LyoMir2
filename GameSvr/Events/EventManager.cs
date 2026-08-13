using SystemModule;

namespace GameSvr
{
    public class EventManager
    {
        private readonly IList<Event> _eventList = null;
        private readonly IList<Event> _closedEventList = null;
        private int _runTick;

        public void Run()
        {
            Run(HUtil32.GetTickCount());
        }

        internal void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - _runTick)) > 250u)
            {
                _runTick = currentTick;
                for (var i = 0; i < _eventList.Count;)
                {
                    Event executeEvent = _eventList[i];
                    executeEvent.Run(currentTick);
                    if (executeEvent.m_boClosed)
                    {
                        executeEvent.m_dwCloseTick = currentTick;
                        _closedEventList.Add(executeEvent);
                        _eventList.RemoveAt(i);
                        continue;
                    }
                    i++;
                }
            }

            // Native sub_718724. Both loop bounds are confirmed byte-for-byte:
            //   0x718735  33 FF                 xor edi,edi        removeCount = 0
            //   0x71875E  2B 43 04              sub eax,[ebx+4]    now - closeTick
            //   0x718761  3D E0 93 04 00        cmp eax,0x493E0    300000 ms
            //   0x718766  73 0A                 jae -> reap; else fall through to
            //   0x718770  EB 6E                 jmp 0x7187E0       BREAK
            //   0x7187CD  47                    inc edi
            //   0x7187CE  83 FF 0A              cmp edi,0xA
            //   0x7187D1  74 0D                 je 0x7187E0        BREAK at 10
            //   0x7187D3  83 78 20 00 / 0F 85   loop while head != nil
            // So the ten-per-tick cap is real, and the retention test is
            // `elapsed >= 300000` (jae), i.e. break on strictly less. Native
            // increments then compares == 10 where this checks < 10 at the top;
            // both process at most ten. Native also reads the close tick from the
            // list NODE at [node+4] rather than from the event, which is the same
            // value because the node is built at enqueue time.
            var removeCount = 0;
            while (removeCount < 10 && _closedEventList.Count > 0)
            {
                Event closedEvent = _closedEventList[0];
                if (unchecked((uint)(currentTick - closedEvent.m_dwCloseTick)) < 300000u)
                {
                    break;
                }
                _closedEventList.RemoveAt(0);
                removeCount++;
            }
        }

        public Event GetEvent(Envirnoment Envir, int nX, int nY, int nType)
        {
            Event result = null;
            for (var i = _eventList.Count - 1; i >= 0; i--)
            {
                Event currentEvent = _eventList[i];
                if (currentEvent.m_nEventType == nType)
                {
                    if (currentEvent.m_Envir == Envir && currentEvent.m_nX == nX && currentEvent.m_nY == nY)
                    {
                        result = currentEvent;
                        break;
                    }
                }
            }
            return result;
        }

        public void AddEvent(Event @event)
        {
            if (@event == null)
            {
                return;
            }
            _eventList.Add(@event);
        }

        internal bool ContainsEventExact(Event expectedEvent)
        {
            if (expectedEvent == null) return false;

            for (var i = _eventList.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_eventList[i], expectedEvent))
                    return true;
            }
            for (var i = _closedEventList.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_closedEventList[i], expectedEvent))
                    return true;
            }
            return false;
        }

        internal void DiscardEventExact(Event expectedEvent)
        {
            if (expectedEvent == null) return;

            for (var i = _eventList.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_eventList[i], expectedEvent))
                    _eventList.RemoveAt(i);
            }
            for (var i = _closedEventList.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_closedEventList[i], expectedEvent))
                    _closedEventList.RemoveAt(i);
            }
        }

        internal void DiscardClosedEvent(Event expectedEvent)
        {
            if (expectedEvent == null || !expectedEvent.m_boClosed) return;
            DiscardEventExact(expectedEvent);
        }

        internal int CloseEventsForEnvironment(Envirnoment environment, int? eventType = null)
        {
            if (environment == null)
            {
                return 0;
            }

            if (eventType == 0)
            {
                eventType = null;
            }

            var closedCount = 0;
            for (var i = _eventList.Count - 1; i >= 0; i--)
            {
                Event currentEvent = _eventList[i];
                if (currentEvent.m_boClosed ||
                    !ReferenceEquals(currentEvent.m_Envir, environment) ||
                    (eventType.HasValue && currentEvent.m_nEventType != eventType.Value))
                {
                    continue;
                }

                currentEvent.Close();
                closedCount++;
            }
            return closedCount;
        }

        public EventManager()
        {
            _eventList = new List<Event>();
            _closedEventList = new List<Event>();
            _runTick = HUtil32.GetTickCount();
        }
    }
}
