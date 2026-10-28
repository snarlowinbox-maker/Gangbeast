using System;
using UnityEngine;

namespace BeefCity.AI
{
    public enum NodeStatus { Success, Failure, Running }

    /// <summary>
    /// A tiny modular behavior tree. T is the blackboard/context the nodes read and write (EnemyBrain here),
    /// so the same nodes can drive any kind of agent.
    /// </summary>
    public abstract class BTNode<T>
    {
        public abstract NodeStatus Tick(T ctx);
        /// <summary>Called when this node is interrupted or finishes, so it can drop any internal state.</summary>
        public virtual void Reset() { }
    }

    /// <summary>
    /// Runs children in order and fails as soon as one fails. Re-evaluates from the first child every tick, so
    /// leading Conditions act as live guards for the Do node after them.
    /// </summary>
    public sealed class Sequence<T> : BTNode<T>
    {
        readonly BTNode<T>[] _children;
        public Sequence(params BTNode<T>[] children) { _children = children; }

        public override NodeStatus Tick(T ctx)
        {
            for (int i = 0; i < _children.Length; i++)
            {
                NodeStatus s = _children[i].Tick(ctx);
                if (s != NodeStatus.Success)
                {
                    // Children after the one that stopped us are no longer active.
                    for (int j = i + 1; j < _children.Length; j++) _children[j].Reset();
                    return s;
                }
            }
            return NodeStatus.Success;
        }

        public override void Reset()
        {
            foreach (BTNode<T> c in _children) c.Reset();
        }
    }

    /// <summary>
    /// Priority selector: tries children in order and returns the first that does not fail. When a higher-priority
    /// child takes over from a running one, the one it interrupted is reset.
    /// </summary>
    public sealed class Selector<T> : BTNode<T>
    {
        readonly BTNode<T>[] _children;
        int _active = -1;

        public Selector(params BTNode<T>[] children) { _children = children; }

        public override NodeStatus Tick(T ctx)
        {
            for (int i = 0; i < _children.Length; i++)
            {
                NodeStatus s = _children[i].Tick(ctx);
                if (s == NodeStatus.Failure) continue;

                if (_active >= 0 && _active != i) _children[_active].Reset();
                _active = s == NodeStatus.Running ? i : -1;
                return s;
            }
            if (_active >= 0) _children[_active].Reset();
            _active = -1;
            return NodeStatus.Failure;
        }

        public override void Reset()
        {
            if (_active >= 0) _children[_active].Reset();
            _active = -1;
        }
    }

    public sealed class Inverter<T> : BTNode<T>
    {
        readonly BTNode<T> _child;
        public Inverter(BTNode<T> child) { _child = child; }

        public override NodeStatus Tick(T ctx)
        {
            NodeStatus s = _child.Tick(ctx);
            if (s == NodeStatus.Success) return NodeStatus.Failure;
            if (s == NodeStatus.Failure) return NodeStatus.Success;
            return s;
        }

        public override void Reset() { _child.Reset(); }
    }

    /// <summary>Turns a child's failure into success (optional behaviors).</summary>
    public sealed class Succeeder<T> : BTNode<T>
    {
        readonly BTNode<T> _child;
        public Succeeder(BTNode<T> child) { _child = child; }

        public override NodeStatus Tick(T ctx)
        {
            NodeStatus s = _child.Tick(ctx);
            return s == NodeStatus.Running ? s : NodeStatus.Success;
        }

        public override void Reset() { _child.Reset(); }
    }

    /// <summary>Leaf that succeeds when the predicate is true, otherwise fails.</summary>
    public sealed class Condition<T> : BTNode<T>
    {
        readonly Func<T, bool> _test;
        public Condition(Func<T, bool> test) { _test = test; }
        public override NodeStatus Tick(T ctx) => _test(ctx) ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>Leaf that runs an action and returns its status.</summary>
    public sealed class Do<T> : BTNode<T>
    {
        readonly Func<T, NodeStatus> _action;
        readonly Action _onReset;

        /// <param name="onReset">Optional cleanup when a higher-priority branch interrupts this action.</param>
        public Do(Func<T, NodeStatus> action, Action onReset = null)
        {
            _action = action;
            _onReset = onReset;
        }

        public override NodeStatus Tick(T ctx) => _action(ctx);

        public override void Reset()
        {
            if (_onReset != null) _onReset();
        }
    }

    /// <summary>After the child succeeds, fails for the given number of seconds (rate limiting an attack).</summary>
    public sealed class Cooldown<T> : BTNode<T>
    {
        readonly Func<T, float> _seconds;
        readonly BTNode<T> _child;
        float _readyAt;

        public Cooldown(Func<T, float> seconds, BTNode<T> child)
        {
            _seconds = seconds;
            _child = child;
        }

        public override NodeStatus Tick(T ctx)
        {
            if (Time.time < _readyAt) return NodeStatus.Failure;
            NodeStatus s = _child.Tick(ctx);
            if (s == NodeStatus.Success) _readyAt = Time.time + _seconds(ctx);
            return s;
        }

        public override void Reset() { _child.Reset(); }
    }

    /// <summary>Returns Running for the given duration, then Success.</summary>
    public sealed class Wait<T> : BTNode<T>
    {
        readonly Func<T, float> _duration;
        float _until = -1f;

        public Wait(Func<T, float> duration) { _duration = duration; }

        public override NodeStatus Tick(T ctx)
        {
            if (_until < 0f) _until = Time.time + _duration(ctx);
            if (Time.time < _until) return NodeStatus.Running;
            _until = -1f;
            return NodeStatus.Success;
        }

        public override void Reset() { _until = -1f; }
    }
}
