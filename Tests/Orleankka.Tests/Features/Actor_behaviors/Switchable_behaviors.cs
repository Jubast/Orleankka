﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using NUnit.Framework;

namespace Orleankka.Features.Actor_behaviors
{   
    using Behaviors;

    namespace Switchable_behaviors
    {
        [TestFixture]
        class Tests
        {
            List<string> events;

            void AssertEvents(params string[] expected) => 
                CollectionAssert.AreEqual(expected, events);

            [SetUp]
            public void SetUp() => 
                events = new List<string>();

            [Test]
            public void When_not_specified()
            {
                var behavior = new Behavior();
                Assert.That(behavior.Current, Is.Null);
                Assert.That(behavior.Etag, Is.Null);
            }

            [Test]
            public void When_setting_initial_more_than_once()
            {
                var behavior = new Behavior();
                behavior.Initial(message => TaskResult.Done);
                Assert.Throws<InvalidOperationException>(() => behavior.Initial(message => TaskResult.Done));
            }

            [Test]
            public void When_trying_to_become_other_without_setting_initial_first()
            {
                var behavior = new Behavior();
                Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Become(x => TaskResult.Done));
            }

            [Test]
            public void When_setting_initial()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("Initial");

                behavior.Initial("Initial");

                Assert.That(behavior.Current.Name, Is.EqualTo("Initial"));
                Assert.That(events, Has.Count.EqualTo(0),
                    "OnBecome should not be called when setting initial");
                Assert.That(behavior.Etag, Is.Not.Null);
            }

            [Test]
            public async Task When_receiving_activate_message_initial()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("Initial");

                behavior.Initial("Initial");

                var result = await behavior.Receive(Activate.State);
                Assert.That(result, Is.SameAs(Done.Result));
                
                Assert.That(behavior.Current.Name, Is.EqualTo("Initial"));
                AssertEvents("OnActivate_Initial");
            }

            [Test]
            public async Task When_receiving_deactivate_message_initial()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("Initial");

                behavior.Initial("Initial");

                var result = await behavior.Receive(Deactivate.State);
                Assert.That(result, Is.SameAs(Done.Result));
                
                Assert.That(behavior.Current.Name, Is.EqualTo("Initial"));
                AssertEvents("OnDeactivate_Initial");
            }

            [Test]
            public void When_receiving_behavior_messages_directly()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("Initial");

                behavior.Initial("Initial");

                Assert.ThrowsAsync<InvalidOperationException>(()=> behavior.Receive(Become.Message));
                Assert.ThrowsAsync<InvalidOperationException>(()=> behavior.Receive(Unbecome.Message));

                Assert.That(behavior.Current.Name, Is.EqualTo("Initial"));
                Assert.That(events, Has.Count.EqualTo(0));
            }
            
            [Test]
            public async Task When_transitioning()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("Initial")
                    .State("A");

                behavior.Initial("Initial");
                var etag = behavior.Etag;

                await behavior.Become("A");
                Assert.That(behavior.Current.Name, Is.EqualTo("A"));
                Assert.That(behavior.Etag, Is.Not.Null);
                Assert.That(behavior.Etag, Is.Not.EqualTo(etag));

                var expected = new[]
                {
                    "OnDeactivate_Initial",
                    "OnUnbecome_Initial",
                    "OnBecome_A",
                    "OnActivate_A"
                };

                AssertEvents(expected);
            }

            [Test]
            public async Task When_become_stacked()
            {
                Behavior behavior = new BehaviorTester(events)
                    .Initial("Initial")                    
                    .State("Initial")
                    .State("A")
                    .State("B")
                    .State("C");

                await behavior.BecomeStacked("A");
                await behavior.BecomeStacked("B");
                await behavior.BecomeStacked("C");
                Assert.That(behavior.Current.Name, Is.EqualTo("C"));

                await behavior.Unbecome();
                Assert.That(behavior.Current.Name, Is.EqualTo("B"));

                await behavior.Unbecome();
                Assert.That(behavior.Current.Name, Is.EqualTo("A"));

                await behavior.Unbecome();
                Assert.That(behavior.Current.Name, Is.EqualTo("Initial"));

                Assert.ThrowsAsync<InvalidOperationException>(()=> behavior.Unbecome());                
            }

            [Test]
            public void When_returns_null_task()
            {
                Behavior behavior = new BehaviorTester(events)
                    .State("A", x => null)
                    .Initial("A");

                var exception = Assert.ThrowsAsync<InvalidOperationException>(async ()=> await behavior.Receive("foo"));
                Assert.That(exception.Message, Is.EqualTo("Behavior returns null task on handling 'foo' message"));
            }

            [Test]
            public async Task When_receiving_message()
            {
                Task<object> Receive(object x) => x is string 
                    ? Task.FromResult<object>("foo") 
                    : Task.FromResult<object>("bar");

                Behavior behavior = new BehaviorTester(events)
                    .State("A", Receive);

                behavior.Initial("A");

                Assert.That(await behavior.Receive("1"), Is.EqualTo("foo"));
                Assert.That(await behavior.Receive(1), Is.EqualTo("bar"));
            }

            [Test]
            [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
            public void When_becoming_other_during_transition()
            {
                async Task<object> AttemptBecomeDuring<T>(Behavior b, string other, object message)
                {
                    if (message is T)
                        await b.Become(other);

                    return null;
                }

                Behavior behavior = null;

                behavior = new BehaviorTester(events)
                    .State("A", x => AttemptBecomeDuring<Deactivate>(behavior, "C", x))
                    .State("B")
                    .State("C")
                    .Initial("A");

                Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Become("B"));

                behavior = new BehaviorTester(events)
                    .State("A", x => AttemptBecomeDuring<Unbecome>(behavior, "C", x))
                    .State("B")
                    .State("C")
                    .Initial("A");
               
                Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Become("B"));

                behavior = new BehaviorTester(events)
                    .State("A")
                    .State("B", x => AttemptBecomeDuring<Activate>(behavior, "C", x))
                    .State("C")
                    .Initial("A");  
               
                Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Become("B"));

                behavior = new BehaviorTester(events)
                    .State("A")
                    .State("B", x => AttemptBecomeDuring<Become>(behavior, "C", x))
                    .State("C")
                    .Initial("A");
               
                Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Become("B"));
            }

            [Test]
            public async Task When_become_with_arguments()
            {
                string passedArg = null;

                Task<object> Receive(object message)
                {
                    if (message is Become<string> m)
                        passedArg = m.Argument;

                    return TaskResult.Done;
                };

                Behavior behavior = new BehaviorTester(events)
                    .State("A", Receive)
                    .State("B", Receive)
                    .Initial("A");

                await behavior.Become("B", "arg1");
                Assert.That(passedArg, Is.EqualTo("arg1"));

                await behavior.BecomeStacked("A", "arg2");
                Assert.That(passedArg, Is.EqualTo("arg2"));
            }
        }
    }
}