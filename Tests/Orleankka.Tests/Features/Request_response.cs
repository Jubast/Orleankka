﻿using System;
using System.Threading.Tasks;

using NUnit.Framework;

using Orleans;

namespace Orleankka.Features
{
    namespace Request_response
    {
        using Meta;
        using Testing;

        //[Serializable] public record SetText(string Text) : Command;
        [Serializable]
        public class SetText : Command
        {
            public readonly string Text;

            public SetText(string text)
            {
                Text = text;
            }
        }

        [Serializable]
        public class GetText : Query<string>
        {}

        public interface ITestActor : IActorGrain, IGrainWithStringKey
        {}

        public class TestActor : DispatchActorGrain, ITestActor
        {
            string text = "";

            public void On(SetText cmd) => text = cmd.Text;
            public string On(GetText q) => text;
        }

        [Serializable]
        public class DoTell : Command
        {
            public ActorRef Target;
            public object Message;
        }

        [Serializable]
        public class DoAsk : Query<string>
        {
            public ActorRef Target;
            public object Message;
        }

        public interface ITestInsideActor : IActorGrain, IGrainWithStringKey
        {}

        public class TestInsideActor : DispatchActorGrain, ITestInsideActor
        {
            public async Task Handle(DoTell cmd) => await cmd.Target.Tell(cmd.Message);
            public Task<string> Handle(DoAsk query) => query.Target.Ask<string>(query.Message);
        }

        [TestFixture]
        [RequiresSilo]
        public class Tests
        {
            IActorSystem system;

            [SetUp]
            public void SetUp()
            {
                system = TestActorSystem.Instance;
            }

            [Test]
            public async Task Client_to_actor()
            {
                var actor = system.FreshActorOf<ITestActor>();

                await actor.Tell(new SetText("c-a"));
                Assert.AreEqual("c-a", await actor.Ask(new GetText()));
            }

            [Test]
            public async Task Actor_to_actor()
            {
                var one = system.FreshActorOf<ITestInsideActor>();
                var another = system.FreshActorOf<ITestActor>();

                await one.Tell(new DoTell {Target = another, Message = new SetText("a-a")});
                Assert.AreEqual("a-a", await one.Ask(new DoAsk {Target = another, Message = new GetText()}));
            }
        }
    }
}