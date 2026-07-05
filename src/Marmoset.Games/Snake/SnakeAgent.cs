using System;
using Marmoset.Core;
using Marmoset.Games.Common;

namespace Marmoset.Games.Snake
{
    /// <summary>
    /// Snake 的 RL 环境封装。构造签名与 Game 属性是稳定契约（GH 组件依赖）。
    ///
    /// 最终设计（Games 工作包定稿）：
    ///
    /// 观测（11 维，全部为 0/1 布尔）：
    ///   [0..2]  危险检测：按当前朝向 直行 / 左转 / 右转 一步是否会死（撞墙或撞身体，
    ///           与 SnakeGame.Advance 的碰撞判定一致，wrap 模式下无撞墙）。
    ///   [3..6]  当前朝向 one-hot：Up / Down / Left / Right。
    ///   [7..10] 食物相对方位：food 在头的 上(Y更大) / 下 / 左(X更小) / 右。同轴时对应两位均为 0。
    ///
    /// 动作 Discrete(3)（相对方向，天然不存在 180° 回头非法动作）：
    ///   0 = 左转，1 = 直行，2 = 右转。
    ///
    /// 奖励：
    ///   吃到食物 +1；死亡 -1；每步恒定 -0.001（鼓励尽快找食物）。
    ///
    /// 回合终止：
    ///   死亡 → EndEpisode（terminated）；
    ///   饥饿：连续 width*height*2 步没吃到食物 → EndEpisode（防绕圈刷步数，不追加惩罚）；
    ///   MaxSteps = width*height*8 → TrainingSession 截断（truncated）保底。
    ///
    /// 重置语义：
    ///   OnEpisodeBegin(seed) 调用 SnakeGame.Reset 原地复位（不换 Game 实例，可视化持有引用）。
    ///   seed 为 null 时传 0，FoodSpawner 对 0 使用非确定性随机源。
    ///   步进通过新增的 SnakeGame.StepIn(direction) 驱动，绕过键盘输入的
    ///   pendingDirection / 未 started 语义。
    /// </summary>
    public class SnakeAgent : Agent
    {
        private readonly ObservationSpec _observationSpec = new ObservationSpec(11);
        private readonly ActionSpec _actionSpec = ActionSpec.Discrete(3);
        private int _stepsSinceFood;

        public SnakeAgent(int width = 12, int height = 12, bool wrap = false)
        {
            Width = Math.Max(4, width);
            Height = Math.Max(4, height);
            Wrap = wrap;
            Game = new SnakeGame(Width, Height, seed: 0, wrap: Wrap);
        }

        public int Width { get; }

        public int Height { get; }

        public bool Wrap { get; }

        /// <summary>底层游戏状态。可视化读取时须 lock(TrainingSession.SyncRoot)。</summary>
        public SnakeGame Game { get; }

        /// <summary>连续多少步没吃到食物即饥饿终止。</summary>
        public int HungerLimit => Width * Height * 2;

        public override ObservationSpec ObservationSpec => _observationSpec;

        public override ActionSpec ActionSpec => _actionSpec;

        public override int MaxSteps => Width * Height * 8;

        public override void OnEpisodeBegin(int? seed)
        {
            // seed == 0 时 FoodSpawner 使用非确定性随机源，正好实现 null → 随机种子。
            Game.Reset(Width, Height, seed ?? 0, Wrap);
            _stepsSinceFood = 0;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            var dir = Game.CurrentDirection;

            // 危险检测：直行 / 左转 / 右转
            sensor.AddObservation(Game.WouldDie(dir));
            sensor.AddObservation(Game.WouldDie(LeftOf(dir)));
            sensor.AddObservation(Game.WouldDie(RightOf(dir)));

            // 当前朝向 one-hot
            sensor.AddObservation(dir == Direction.Up);
            sensor.AddObservation(dir == Direction.Down);
            sensor.AddObservation(dir == Direction.Left);
            sensor.AddObservation(dir == Direction.Right);

            // 食物相对方位
            var head = Game.HeadPosition;
            var food = Game.Food;
            sensor.AddObservation(food.Y > head.Y);
            sensor.AddObservation(food.Y < head.Y);
            sensor.AddObservation(food.X < head.X);
            sensor.AddObservation(food.X > head.X);
        }

        public override void OnActionReceived(ActionBuffer actions)
        {
            if (Game.GameOver)
            {
                // 防御：会话未及时 Reset 时不再累积惩罚。
                EndEpisode();
                return;
            }

            var current = Game.CurrentDirection;
            Direction target;
            switch (actions.Discrete[0])
            {
                case 0: target = LeftOf(current); break;
                case 2: target = RightOf(current); break;
                default: target = current; break;
            }

            int previousScore = Game.Score;
            Game.StepIn(target);

            AddReward(-0.001f);

            if (Game.GameOver)
            {
                AddReward(-1f);
                EndEpisode();
                return;
            }

            if (Game.Score > previousScore)
            {
                AddReward(1f);
                _stepsSinceFood = 0;
            }
            else
            {
                _stepsSinceFood++;
                if (_stepsSinceFood >= HungerLimit)
                    EndEpisode();
            }
        }

        private static Direction LeftOf(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Direction.Left;
                case Direction.Left: return Direction.Down;
                case Direction.Down: return Direction.Right;
                case Direction.Right: return Direction.Up;
                default: return Direction.Up;
            }
        }

        private static Direction RightOf(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Direction.Right;
                case Direction.Right: return Direction.Down;
                case Direction.Down: return Direction.Left;
                case Direction.Left: return Direction.Up;
                default: return Direction.Up;
            }
        }
    }
}
