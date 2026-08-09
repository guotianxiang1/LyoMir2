


































namespace GameSvr
{
    
    public class TPathMapCell
    {
        
        public int Distance;
        
        public int Direction;
    }

    public class TPathMap
    {
        public TPathMapCell[,] PathMapArray;
        
        
        
        public int Height = 0;
        
        
        
        public int Width = 0;
        public TGetCostFunc GetCostFunc = null;
        public TRect ClientRect = null;
        
        
        
        public int ScopeValue = 0;
        
        
        
        public bool StartFind = false;

        public TPathMap() : base()
        {
            ScopeValue = 1000; 
            GetCostFunc = null;
        }

        
        
        
        
        
        
        private short DirToDX(int Direction)
        {
            short result;
            switch (Direction)
            {
                case 0:
                case 4:
                    result = 0;
                    break;
                
                case 1:
                    result = 1;
                    break;
                default:
                    result = -1;
                    break;
            }
            return result;
        }

        private short DirToDY(int Direction)
        {
            short result;
            switch (Direction)
            {
                case 2:
                case 6:
                    result = 0;
                    break;
                
                case 3:
                    result = 1;
                    break;
                default:
                    result = -1;
                    break;
            }
            return result;
        }

        
        
        
        public PointInfo[] FindPathOnMap(short X, short Y, bool Run)
        {
            PointInfo[] result = null;
            int Direction;
            int nCount = 0;
            short nX = LoaclX(X);
            short nY = LoaclY(Y);
            if ((nX < 0) || (nY < 0) || (nX >= ClientRect.Right - ClientRect.Left) || (nY >= ClientRect.Bottom - ClientRect.Top))
            {
                return result;
            }
            if ((PathMapArray.Length <= 0) || (PathMapArray[nY, nX].Distance < 0))
            {
                return result;
            }
            result = new PointInfo[PathMapArray[nY, nX].Distance + 1];
            while (PathMapArray[nY, nX].Distance > 0)
            {
                if (!StartFind)
                {
                    break;
                }
                result[PathMapArray[nY, nX].Distance] = new PointInfo(nX, nY);
                Direction = PathMapArray[nY, nX].Direction;
                nX = (short)(nX - DirToDX(Direction));
                nY = (short)(nY - DirToDY(Direction));
                nCount++;
            }
            result[0] = new PointInfo(nX, nY);
            if (Run)
            {
                result = WalkToRun(result);
            }
            return result;
        }

        
        public byte WalkToRun_GetNextDirection(int sx, int sy, int dx, int dy)
        {
            byte result;
            int flagx;
            int flagy;
            const int DR_UP = 0;
            const int DR_UPRIGHT = 1;
            const int DR_RIGHT = 2;
            const int DR_DOWNRIGHT = 3;
            const int DR_DOWN = 4;
            const int DR_DOWNLEFT = 5;
            const int DR_LEFT = 6;
            const int DR_UPLEFT = 7;
            result = DR_DOWN;
            if (sx < dx)
            {
                flagx = 1;
            }
            else if (sx == dx)
            {
                flagx = 0;
            }
            else
            {
                flagx = -1;
            }
            if (Math.Abs(sy - dy) > 2)
            {
                if ((sx >= dx - 1) && (sx <= dx + 1))
                {
                    flagx = 0;
                }
            }
            if (sy < dy)
            {
                flagy = 1;
            }
            else if (sy == dy)
            {
                flagy = 0;
            }
            else
            {
                flagy = -1;
            }
            if (Math.Abs(sx - dx) > 2)
            {
                if ((sy > dy - 1) && (sy <= dy + 1))
                {
                    flagy = 0;
                }
            }
            if ((flagx == 0) && (flagy == -1))
            {
                result = DR_UP;
            }
            if ((flagx == 1) && (flagy == -1))
            {
                result = DR_UPRIGHT;
            }
            if ((flagx == 1) && (flagy == 0))
            {
                result = DR_RIGHT;
            }
            if ((flagx == 1) && (flagy == 1))
            {
                result = DR_DOWNRIGHT;
            }
            if ((flagx == 0) && (flagy == 1))
            {
                result = DR_DOWN;
            }
            if ((flagx == -1) && (flagy == 1))
            {
                result = DR_DOWNLEFT;
            }
            if ((flagx == -1) && (flagy == 0))
            {
                result = DR_LEFT;
            }
            if ((flagx == -1) && (flagy == -1))
            {
                result = DR_UPLEFT;
            }
            return result;
        }

        public PointInfo[] WalkToRun(PointInfo[] Path)
        {
            PointInfo[] result;
            int nDir1;
            int nDir2;
            int I;
            int n01;
            PointInfo[] WalkPath;
            int nStep;
            result = null;
            WalkPath = null;
            if ((Path != null) && (Path.Length > 1))
            {
                WalkPath = new PointInfo[Path.Length];
                WalkPath = Path;
                nStep = 0;
                I = 0;
                while (true)
                {
                    if (!StartFind)
                    {
                        break;
                    }
                    if (I >= WalkPath.Length)
                    {
                        break;
                    }
                    if (nStep >= 2)
                    {
                        nDir1 = WalkToRun_GetNextDirection(WalkPath[I - 2].nX, WalkPath[I - 2].nY, WalkPath[I - 1].nX, WalkPath[I - 1].nY);
                        nDir2 = WalkToRun_GetNextDirection(WalkPath[I - 1].nX, WalkPath[I - 1].nY, WalkPath[I].nX, WalkPath[I].nY);
                        if (nDir1 == nDir2)
                        {
                            WalkPath[I - 1].nX = -1;
                            WalkPath[I - 1].nY = -1;
                            nStep = 0;
                        }
                        else
                        {
                            
                            I -= 1;
                            nStep = 0;
                            continue;
                        }
                    }
                    nStep++;
                    I++;
                }
                n01 = 0;
                for (I = 0; I < WalkPath.Length; I++)
                {
                    if ((WalkPath[I].nX != -1) && (WalkPath[I].nY != -1))
                    {
                        n01++;
                        result = new PointInfo[n01];
                        result[n01 - 1] = WalkPath[I];
                    }
                }
                return result;
            }
            if ((Path != null) && (Path.Length > 0))
            {
                result = new PointInfo[Path.Length - 1];
                for (I = 1; I < Path.Length; I++)
                {
                    result[I - 1] = Path[I];
                }
            }
            else
            {
                result = new PointInfo[0];
                result = null;
            }
            return result;
        }

        
        
        
        
        
        public int MapX(int X)
        {
            return X + ClientRect.Left;
        }

        public int MapY(int Y)
        {
            return Y + ClientRect.Top;
        }

        public short LoaclX(short X)
        {
            return (short)(X - ClientRect.Left);
        }

        public short LoaclY(short Y)
        {
            return (short)(Y - ClientRect.Top);
        }

        public void GetClientRect(int X, int Y)
        {
            ClientRect = new TRect(0, 0, Width, Height);
            
            if (Width > ScopeValue)
            {
                ClientRect.Left = Math.Max(0, X - ScopeValue / 2);
                ClientRect.Right = ClientRect.Left + Math.Min(Width, X + ScopeValue / 2);
            }
            if (Height > ScopeValue)
            {
                ClientRect.Top = Math.Max(0, Y - ScopeValue / 2);
                ClientRect.Bottom = ClientRect.Top + Math.Min(Height, Y + ScopeValue / 2);
            }
        }

        public void GetClientRect(int X1, int Y1, int X2, int Y2)
        {
            int X;
            int Y;
            if (X1 > X2)
            {
                X = X2 + (X1 - X2) / 2;
            }
            else if (X1 < X2)
            {
                X = X1 + (X2 - X1) / 2;
            }
            else
            {
                X = X1;
            }
            if (Y1 > Y2)
            {
                Y = Y2 + (Y1 - Y2) / 2;
            }
            else if (Y1 < Y2)
            {
                Y = Y1 + (Y2 - Y1) / 2;
            }
            else
            {
                Y = Y1;
            }
            ClientRect = new TRect(0, 0, Width, Height);
            if (Width > ScopeValue)
            {
                ClientRect.Left = Math.Max(0, X - ScopeValue / 2);
                ClientRect.Right = ClientRect.Left + Math.Min(Width, X + ScopeValue / 2);
            }
            if (Height > ScopeValue)
            {
                ClientRect.Top = Math.Max(0, Y - ScopeValue / 2);
                ClientRect.Bottom = ClientRect.Top + Math.Min(Height, Y + ScopeValue / 2);
            }
        }

        
        
        
        
        public void FillPathMap_PreparePathMap(ref TPathMapCell[,] result)
        {
            int nWidth = ClientRect.Right - ClientRect.Left;
            int nHeight = ClientRect.Bottom - ClientRect.Top;
            result = new TPathMapCell[nHeight, nWidth];
            for (var Y = 0; Y < nHeight; Y++)
            {
                for (var X = 0; X < nWidth; X++)
                {
                    result[Y, X] = new TPathMapCell();
                    result[Y, X].Distance = -1;
                }
            }
        }

        
        
        public void FillPathMap_TestNeighbours(TWave OldWave, TWave NewWave, ref TPathMapCell[,] result)
        {
            int X;
            int Y;
            int C;
            int D;
            for (D = 0; D <= 7; D++)
            {
                X = OldWave.Item.X + DirToDX(D);
                Y = OldWave.Item.Y + DirToDY(D);
                C = GetCost(X, Y, D);
                if ((C >= 0) && (result[Y, X].Distance < 0))
                {
                    NewWave.Add(X, Y, C, D);
                }
            }
        }

        public void FillPathMap_ExchangeWaves(TWave OldWave, TWave NewWave)
        {
            TWave W;
            W = OldWave;
            OldWave = NewWave;
            NewWave = W;
            NewWave.Clear();
        }

        
        
        
        
        protected TPathMapCell[,] FillPathMap(short X1, short Y1, short X2, short Y2)
        {
            TPathMapCell[,] result = null;
            TWave OldWave;
            TWave NewWave;
            bool Finished;
            TWaveCell I;
            GetClientRect(X1, Y1);
            short nX1 = LoaclX(X1);
            short nY1 = LoaclY(Y1);
            short nX2 = LoaclX(X2);
            short nY2 = LoaclY(Y2);
            if (X2 < 0)
            {
                nX2 = X2;
            }
            if (Y2 < 0)
            {
                nY2 = Y2;
            }
            if ((X2 >= 0) && (Y2 >= 0))
            {
                if ((Math.Abs(nX1 - nX2) > (ClientRect.Right - ClientRect.Left)) || (Math.Abs(nY1 - nY2) > (ClientRect.Bottom - ClientRect.Top)))
                {
                    result = new TPathMapCell[0, 0];
                    return result;
                }
            }
            FillPathMap_PreparePathMap(ref result);
            
            OldWave = new TWave();
            NewWave = new TWave();
            try
            {
                result[nY1, nX1].Distance = 0;
                
                OldWave.Add(nX1, nY1, 0, 0);
                
                FillPathMap_TestNeighbours(OldWave, NewWave, ref result);
                Finished = ((nX1 == nX2) && (nY1 == nY2));
                
                while (!Finished)
                {
                    FillPathMap_ExchangeWaves(OldWave, NewWave);
                    if (!StartFind)
                    {
                        break;
                    }
                    if (!OldWave.Start())
                    {
                        break;
                    }
                    do
                    {
                        if (!StartFind)
                        {
                            break;
                        }
                        I = OldWave.Item;
                        I.Cost = I.Cost - OldWave.MinCost;
                        
                        if (I.Cost > 0)
                        {
                            
                            
                            NewWave.Add(I.X, I.Y, I.Cost, I.Direction);
                        }
                        else
                        {
                            
                            if (result[I.Y, I.X].Distance >= 0)
                            {
                                continue;
                            }
                            result[I.Y, I.X].Distance = result[I.Y - DirToDY(I.Direction), I.X - DirToDX(I.Direction)].Distance + 1;
                            
                            result[I.Y, I.X].Direction = I.Direction;
                            Finished = ((I.X == nX2) && (I.Y == nY2));
                            
                            if (Finished)
                            {
                                break;
                            }
                            FillPathMap_TestNeighbours(OldWave, NewWave, ref result);
                        }
                    } while (!(!OldWave.Next()));
                }
            }
            finally
            {

            }
            return result;
        }

        public virtual int GetCost(int X, int Y, int Direction)
        {
            int result;
            Direction = (Direction & 7);
            if ((X < 0) || (X >= Width) || (Y < 0) || (Y >= Height))
            {
                result = -1;
            }
            else
            {
                result = GetCostFunc(X, Y, Direction, 0);
            }
            return result;
        }
    }

    public class TFindPath : TPathMap
    {
        public PointInfo[] Path
        {
            get
            {
                return FPath;
            }
            set
            {
                FPath = value;
            }
        }
        private PointInfo[] FPath;
        private Envirnoment FEnvir = null;
        public int BeginX = 0;
        public int BeginY = 0;
        public int EndX = 0;
        public int EndY = 0;

        public TFindPath() : base()
        {
            this.StartFind = false;
        }

        public void Stop()
        {
            this.StartFind = false;
            BeginX = -1;
            BeginY = -1;
            EndX = -1;
            EndY = -1;
            this.PathMapArray = new TPathMapCell[0, 0];
            this.PathMapArray = null;
        }

        public PointInfo[] FindPath(short StopX, short StopY, bool Run)
        {
            PointInfo[] result;
            EndX = StopX;
            EndY = StopY;
            result = this.FindPathOnMap(StopX, StopY, Run);
            return result;
        }

        public PointInfo[] FindPath(Envirnoment Envir, short StartX, short StartY, short StopX, short StopY, bool Run)
        {
            PointInfo[] result;
            this.Width = Envir.wWidth;
            this.Height = Envir.wHeight;
            BeginX = StartX;
            BeginY = StartY;
            EndX = StopX;
            EndY = StopY;
            FPath = null;
            FEnvir = Envir;
            this.StartFind = true;
            this.PathMapArray = this.FillPathMap(StartX, StartY, StopX, StopY);
            result = this.FindPathOnMap(StopX, StopY, Run);
            return result;
        }

        public void SetStartPos(short StartX, short StartY)
        {
            BeginX = StartX;
            BeginY = StartY;
            this.PathMapArray = this.FillPathMap(StartX, StartY, -1, -1);
        }

        public int GetCost(short X, short Y, int Direction)
        {
            int result;
            int nX;
            int nY;
            if (FEnvir != null)
            {
                Direction = (Direction & 7);
                if ((X < 0) || (X >= this.ClientRect.Right - this.ClientRect.Left) || (Y < 0) || (Y >= this.ClientRect.Bottom - this.ClientRect.Top))
                {
                    result = -1;
                }
                else
                {
                    nX = this.MapX(X);
                    nY = this.MapY(Y);
                    if (FEnvir.CanWalkEx(nX, nY, false))
                    {
                        result = 4;
                    }
                    else
                    {
                        result = -1;
                    }
                    
                    if (((Direction & 1) == 1) && (result > 0))
                    {
                        result = result + (result >> 1); 
                    }
                }
            }
            else
            {
                result = -1;
            }
            return result;
        }
    }

    public struct TWaveCell
    {
        
        public int X;
        public int Y;
        public int Cost;
        public int Direction;
    }

    public class TWave
    {
        public TWaveCell Item => GetItem();
        public int MinCost => FMinCost;

        private TWaveCell[] FData = new TWaveCell[0];
        private int FPos = 0;
        private int FCount = 0;
        private int FMinCost = 0;

        public TWave()
        {
            Clear();
        }

        ~TWave()
        {
            FData = null;
        }

        private TWaveCell GetItem()
        {
            return FData[FPos];
        }

        public void Add(int NewX, int NewY, int NewCost, int NewDirection)
        {
            if (FCount >= FData.Length)
            {
                Array.Resize(ref FData, FData.Length + 30);
            }
            FData[FCount].X = NewX;
            FData[FCount].Y = NewY;
            FData[FCount].Cost = NewCost;
            FData[FCount].Direction = NewDirection;
            if (NewCost < FMinCost)
            {
                FMinCost = NewCost;
            }
            FCount++;
        }

        public void Clear()
        {
            FPos = 0;
            FCount = 0;
            FMinCost = Int32.MaxValue;
        }

        public bool Start()
        {
            FPos = 0;
            return FCount > 0; ;
        }

        public bool Next()
        {
            FPos++;
            return (FPos < FCount);
        }
    }

    public delegate int TGetCostFunc(int X, int Y, int Direction, int PathWidth);
}