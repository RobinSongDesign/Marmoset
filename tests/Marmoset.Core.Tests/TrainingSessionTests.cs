using System;
using Marmoset.Core;
using Xunit;

namespace Marmoset.Core.Tests
{
    public class TrainingSessionTests
    {
        [Fact]
        public void Reset_ReturnsObservation_AndPassesSeed()
        {
            var agent = new FakeAgent(obsLength: 3);
            var session = new TrainingSession(agent);

            float[] observation = session.Reset(seed: 123);

            Assert.Equal(new[] { 0f, -1f, 42f }, observation);
            Assert.Equal(123, agent.LastSeed);
            Assert.Equal(1, agent.EpisodeBeginCount);
            Assert.Equal(0, session.CurrentEpisodeSteps);
            Assert.Equal(0f, session.CurrentEpisodeReward);
        }

        [Fact]
        public void Reset_WithNullSeed_PassesNull()
        {
            var agent = new FakeAgent();
            var session = new TrainingSession(agent);

            session.Reset(seed: 42);
            Assert.Equal(42, agent.LastSeed);

            session.Reset();
            Assert.Null(agent.LastSeed);
        }

        [Fact]
        public void Step_AccumulatesRewardAndCounters()
        {
            var agent = new FakeAgent { RewardPerStep = 0.5f };
            var session = new TrainingSession(agent);
            session.Reset();

            StepResult first = session.Step(ActionBuffer.FromDiscrete(1));
            StepResult second = session.Step(ActionBuffer.FromDiscrete(2));

            Assert.Equal(0.5f, first.Reward);
            Assert.False(first.Terminated);
            Assert.False(first.Truncated);
            Assert.Equal(new[] { 1f, 1f, 42f }, first.Observation);
            Assert.Equal(new[] { 2f, 2f, 42f }, second.Observation);
            Assert.Equal(2, session.CurrentEpisodeSteps);
            Assert.Equal(2, session.TotalSteps);
            Assert.Equal(1.0f, session.CurrentEpisodeReward);
            Assert.Equal(0, session.EpisodeCount);
        }

        [Fact]
        public void Step_TerminatesWhenAgentEndsEpisode()
        {
            var agent = new FakeAgent { EndOnDiscreteAction = 3 };
            var session = new TrainingSession(agent);
            session.Reset();

            StepResult ongoing = session.Step(ActionBuffer.FromDiscrete(1));
            StepResult terminal = session.Step(ActionBuffer.FromDiscrete(3));

            Assert.False(ongoing.Terminated);
            Assert.True(terminal.Terminated);
            Assert.False(terminal.Truncated);
            Assert.Equal(1, session.EpisodeCount);
        }

        [Fact]
        public void Step_TruncatesAtMaxSteps()
        {
            var agent = new FakeAgent(maxSteps: 3);
            var session = new TrainingSession(agent);
            session.Reset();

            StepResult s1 = session.Step(ActionBuffer.FromDiscrete(0));
            StepResult s2 = session.Step(ActionBuffer.FromDiscrete(0));
            StepResult s3 = session.Step(ActionBuffer.FromDiscrete(0));

            Assert.False(s1.Truncated);
            Assert.False(s2.Truncated);
            Assert.True(s3.Truncated);
            Assert.False(s3.Terminated); // 截断不是终止
            Assert.Equal(1, session.EpisodeCount);
        }

        [Fact]
        public void Step_TerminatedTakesPrecedenceOverTruncated()
        {
            var agent = new FakeAgent(maxSteps: 1) { EndOnDiscreteAction = 3 };
            var session = new TrainingSession(agent);
            session.Reset();

            StepResult result = session.Step(ActionBuffer.FromDiscrete(3));

            Assert.True(result.Terminated);
            Assert.False(result.Truncated);
        }

        [Fact]
        public void Reset_ClearsEpisodeState_ButKeepsTotals()
        {
            var agent = new FakeAgent();
            var session = new TrainingSession(agent);
            session.Reset();
            session.Step(ActionBuffer.FromDiscrete(1));
            session.Step(ActionBuffer.FromDiscrete(1));

            float[] observation = session.Reset();

            Assert.Equal(0, session.CurrentEpisodeSteps);
            Assert.Equal(0f, session.CurrentEpisodeReward);
            Assert.Equal(2, session.TotalSteps); // 总步数跨回合累计
            Assert.Equal(new[] { 0f, -1f, 42f }, observation);
        }

        [Fact]
        public void Step_MismatchedAction_ThrowsArgumentException()
        {
            var agent = new FakeAgent(actionSpec: ActionSpec.Discrete(4));
            var session = new TrainingSession(agent);
            session.Reset();

            // 分支数不匹配
            Assert.Throws<ArgumentException>(() => session.Step(ActionBuffer.FromDiscrete(1, 2)));
            // 动作值越界
            Assert.Throws<ArgumentException>(() => session.Step(ActionBuffer.FromDiscrete(4)));
            // 动作类型不匹配
            Assert.Throws<ArgumentException>(() => session.Step(ActionBuffer.FromContinuous(0.5f)));
        }

        [Fact]
        public void Events_FireOnStepAndEpisodeEnd()
        {
            var agent = new FakeAgent { EndOnDiscreteAction = 3 };
            var session = new TrainingSession(agent);
            int stepEvents = 0;
            int episodeEvents = 0;
            session.StepCompleted += () => stepEvents++;
            session.EpisodeCompleted += () => episodeEvents++;

            session.Reset();                              // StepCompleted +1
            session.Step(ActionBuffer.FromDiscrete(1));   // +1
            session.Step(ActionBuffer.FromDiscrete(3));   // +1, EpisodeCompleted +1

            Assert.Equal(3, stepEvents);
            Assert.Equal(1, episodeEvents);
        }
    }
}
