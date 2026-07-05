using System;
using Marmoset.Core;

namespace Marmoset.Games.Tetris
{
    /// <summary>
    /// Tetris 的 RL 环境封装。构造签名与 Game 属性是稳定契约（GH 组件依赖）。
    ///
    /// 最终设计（Games 工作包定稿）：
    ///
    /// 观测（width*height + 8 + 7 维；默认 10x20 → 215 维）：
    ///   [0 .. w*h-1]        锁定格子占用 flatten，索引 y*width + x，占用为 1，空为 0。
    ///   [w*h .. w*h+7]      当前活动块 4 个格子的归一化坐标 (x/width, y/height)，
    ///                       按 TetrisPiece.Cells() 的固定顺序；无活动块时补 0。
    ///                       （二选一方案里选了坐标而非第二层通道，观测更紧凑。）
    ///   [w*h+8 .. w*h+14]   当前块类型 one-hot 7（I O T S Z J L）；无活动块时全 0。
    ///
    /// 动作 Discrete(6)：
    ///   0 = 左移，1 = 右移，2 = 顺时针旋转，3 = 软降，4 = 硬降，5 = 无操作。
    ///   每个动作执行后调用一次重力 TetrisGame.Step()（向 -Y 落一格，落不动则锁块）。
    ///
    /// 奖励：
    ///   消行：按 Score 增量每行 +1（含动作与重力两个阶段产生的消行）；
    ///   游戏结束 -1；每步存活 +0.001。
    ///
    /// 回合终止：
    ///   GameOver → EndEpisode（terminated）；
    ///   MaxSteps = width*height*10（默认 2000 步 ≈ 上百个块）→ TrainingSession 截断保底。
    ///
    /// 重置语义：
    ///   OnEpisodeBegin(seed) 调用 TetrisGame.Start 原地复位（不换 Game 实例，可视化持有引用）。
    ///   seed 为 null 时传 0，TetrisGame 对 0 使用非确定性随机源。
    /// </summary>
    public class TetrisAgent : Agent
    {
        private readonly ObservationSpec _observationSpec;
        private readonly ActionSpec _actionSpec = ActionSpec.Discrete(6);
        private readonly float[] _lockedGrid;

        public TetrisAgent(int width = 10, int height = 20)
        {
            // 宽度至少容纳 I 形（锚点 ±2），高度至少容纳出生区 + 少量堆叠空间。
            Width = Math.Max(5, width);
            Height = Math.Max(6, height);
            Game = new TetrisGame(Width, Height, seed: 0);
            _observationSpec = new ObservationSpec(Width * Height + 8 + 7);
            _lockedGrid = new float[Width * Height];
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>底层游戏状态。可视化读取时须 lock(TrainingSession.SyncRoot)。</summary>
        public TetrisGame Game { get; }

        public override ObservationSpec ObservationSpec => _observationSpec;

        public override ActionSpec ActionSpec => _actionSpec;

        public override int MaxSteps => Width * Height * 10;

        public override void OnEpisodeBegin(int? seed)
        {
            // seed == 0 时 TetrisGame 使用非确定性随机源，正好实现 null → 随机种子。
            Game.Start(Width, Height, seed ?? 0);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // 锁定格子占用通道
            Array.Clear(_lockedGrid, 0, _lockedGrid.Length);
            foreach (var cell in Game.LockedCells())
            {
                if (cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height)
                    _lockedGrid[cell.Y * Width + cell.X] = 1f;
            }
            sensor.AddObservation(_lockedGrid);

            // 活动块 4 格归一化坐标（GameOver 时出生块可能越界，坐标照常归一化，不裁剪）
            var cells = Game.ActiveCells();
            for (int i = 0; i < 4; i++)
            {
                if (i < cells.Count)
                {
                    sensor.AddObservation(cells[i].X / (float)Width);
                    sensor.AddObservation(cells[i].Y / (float)Height);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }
            }

            // 块类型 one-hot
            var type = Game.CurrentPieceType;
            for (int t = 0; t < 7; t++)
                sensor.AddObservation(type.HasValue && (int)type.Value == t);
        }

        public override void OnActionReceived(ActionBuffer actions)
        {
            if (Game.GameOver)
            {
                // 防御：会话未及时 Reset 时不再累积惩罚。
                EndEpisode();
                return;
            }

            int previousScore = Game.Score;

            switch (actions.Discrete[0])
            {
                case 0: Game.ApplyAction(TetrisAction.MoveLeft); break;
                case 1: Game.ApplyAction(TetrisAction.MoveRight); break;
                case 2: Game.ApplyAction(TetrisAction.RotateClockwise); break;
                case 3: Game.ApplyAction(TetrisAction.SoftDrop); break;
                case 4: Game.ApplyAction(TetrisAction.HardDrop); break;
                default: break; // 5 = 无操作
            }

            // 每个动作后固定一次重力步进（GameOver 时内部自动无效）。
            Game.Step();

            AddReward(0.001f);

            int clearedLines = Game.Score - previousScore;
            if (clearedLines > 0)
                AddReward(clearedLines);

            if (Game.GameOver)
            {
                AddReward(-1f);
                EndEpisode();
            }
        }
    }
}
