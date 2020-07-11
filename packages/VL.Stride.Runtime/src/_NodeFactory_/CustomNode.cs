﻿using Stride.Core;
using Stride.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using VL.Core;
using VL.Core.Diagnostics;
using VL.Lib.Collections.TreePatching;
using VL.Lib.Experimental;

namespace VL.Stride
{
    static class FactoryExtensions
    {
        public static CustomNodeDesc<T> NewNode<T>(this IVLNodeDescriptionFactory factory, 
            string name = default, 
            string category = default, 
            bool copyOnWrite = true,
            Action<T> init = default,
            bool hasStateOutput = true,
            bool fragmented = false) 
            where T : new()
        {
            return new CustomNodeDesc<T>(factory, 
                ctor: ctx =>
                {
                    var instance = new T();
                    init?.Invoke(instance);
                    return (instance, default);
                }, 
                name: name, 
                category: category,
                copyOnWrite: copyOnWrite,
                hasStateOutput: hasStateOutput,
                fragmented: fragmented);
        }

        public static CustomNodeDesc<TComponent> NewComponentNode<TComponent>(this IVLNodeDescriptionFactory factory, string category)
            where TComponent : EntityComponent, new()
        {
            return new CustomNodeDesc<TComponent>(factory,
                ctor: nodeContext =>
                {
                    var component = new TComponent();
                    var manager = new TreeNodeParentManager<Entity, EntityComponent>(component, (e, c) => e.Add(c), (e, c) => e.Remove(c));
                    var sender = new Sender<object, object>(nodeContext, component, manager);
                    var cachedMessages = default(List<VL.Lang.Message>);
                    var subscription = manager.ToggleWarning.Subscribe(v => ToggleMessages(v));
                    return (component, () =>
                    {
                        ToggleMessages(false);
                        manager.Dispose();
                        sender.Dispose();
                        subscription.Dispose();
                    }
                    );

                    void ToggleMessages(bool on)
                    {
                        var messages = cachedMessages ?? (cachedMessages = nodeContext.Path.Stack
                            .Select(id => new VL.Lang.Message(id, Lang.MessageSeverity.Warning, "Component should only be connected to one Entity."))
                            .ToList());
                        foreach (var m in messages)
                            VL.Lang.PublicAPI.Session.ToggleMessage(m, on);
                    }
                }, 
                category: category, 
                copyOnWrite: false,
                fragmented: true);
        }

        public static IVLNodeDescription WithEnabledPin<TComponent>(this CustomNodeDesc<TComponent> node)
            where TComponent : ActivableEntityComponent
        {
            return node.AddInput("Enabled", x => x.Enabled, (x, v) => x.Enabled = v, true);
        }
    }

    class CustomNodeDesc<TInstance> : IVLNodeDescription
    {
        readonly List<CustomPinDesc> inputs = new List<CustomPinDesc>();
        readonly List<CustomPinDesc> outputs = new List<CustomPinDesc>();
        readonly Func<NodeContext, (TInstance, Action)> ctor;

        public CustomNodeDesc(IVLNodeDescriptionFactory factory, Func<NodeContext, (TInstance, Action)> ctor, 
            string name = default, 
            string category = default, 
            bool copyOnWrite = true, 
            bool hasStateOutput = true,
            bool fragmented = false)
        {
            Factory = factory;
            this.ctor = ctor;

            Name = name ?? typeof(TInstance).Name;
            Category = category ?? string.Empty;
            CopyOnWrite = copyOnWrite;
            Fragmented = fragmented;

            if (hasStateOutput)
                AddOutput("Output", x => x);
        }

        public IVLNodeDescriptionFactory Factory { get; }

        public string Name { get; }

        public string Category { get; }

        public bool Fragmented { get; }

        public bool CopyOnWrite { get; }

        public IReadOnlyList<IVLPinDescription> Inputs => inputs;

        public IReadOnlyList<IVLPinDescription> Outputs => outputs;

        public IEnumerable<Message> Messages => Enumerable.Empty<Message>();

        public IVLNode CreateInstance(NodeContext context)
        {
            var (instance, onDispose) = ctor(context);

            var node = new Node(context)
            {
                NodeDescription = this
            };

            var inputs = this.inputs.Select(p => p.CreatePin(node, instance)).ToArray();
            var outputs = this.outputs.Select(p => p.CreatePin(node, instance)).ToArray();

            node.Inputs = inputs;
            node.Outputs = outputs;

            if (CopyOnWrite)
            {
                node.updateAction = () =>
                {
                    if (node.needsUpdate)
                    {
                        node.needsUpdate = false;
                        // TODO: Causes render pipeline to crash
                        //if (instance is IDisposable disposable)
                        //    disposable.Dispose();

                        instance = ctor(context).Item1;

                        // Copy the values
                        foreach (var input in inputs)
                            input.Update(instance);

                        foreach (var output in outputs)
                            output.Instance = instance;
                    }
                };
                node.disposeAction = () =>
                {
                    // TODO: Causes render pipeline to crash
                    //if (instance is IDisposable disposable)
                    //    disposable.Dispose();
                };
            }
            else
            {
                node.updateAction = () =>
                {
                    if (node.needsUpdate)
                    {
                        node.needsUpdate = false;
                    }
                };
                node.disposeAction = () =>
                {
                    if (instance is IDisposable disposable)
                        disposable.Dispose();
                    onDispose?.Invoke();
                };
            }
            return node;
        }

        public bool OpenEditor()
        {
            return false;
        }

        public CustomNodeDesc<TInstance> AddInput<T>(string name, Func<TInstance, T> getter, Action<TInstance, T> setter, Func<T, T, bool> equals = default)
        {
            inputs.Add(new CustomPinDesc()
            {
                Name = name.InsertSpaces(),
                Type = typeof(T),
                CreatePin = (node, instance) => new InputPin<T>(node, instance, getter, setter, getter(instance), equals)
            });
            return this;
        }

        public CustomNodeDesc<TInstance> AddInput<T>(string name, Func<TInstance, T> getter, Action<TInstance, T> setter, T defaultValue)
        {
            inputs.Add(new CustomPinDesc()
            {
                Name = name.InsertSpaces(),
                Type = typeof(T),
                DefaultValue = defaultValue,
                CreatePin = (node, instance) => new InputPin<T>(node, instance, getter, setter, defaultValue)
            });
            return this;
        }

        // Hack to workaround equality bug (https://github.com/stride3d/stride/issues/735)
        public CustomNodeDesc<TInstance> AddInputWithRefEquality<T>(string name, Func<TInstance, T> getter, Action<TInstance, T> setter)
            where T : class
        {
            return AddInput(name, getter, setter, equals: ReferenceEqualityComparer<T>.Default.Equals);
        }

        static bool SequenceEqual<T>(IEnumerable<T> a, IEnumerable<T> b)
        {
            if (a is null)
                return b is null;
            if (b is null)
                return false;
            return a.SequenceEqual(b);
        }

        public CustomNodeDesc<TInstance> AddListInput<T>(string name, Func<TInstance, IList<T>> getter)
        {
            return AddInput<IReadOnlyList<T>>(name, 
                getter: instance => (IReadOnlyList<T>)getter(instance),
                equals: SequenceEqual,
                setter: (x, v) =>
                {
                    var currentItems = getter(x);
                    currentItems.Clear();

                    var newItems = v?.Where(i => i != null);
                    if (newItems != null)
                    {
                        foreach (var item in newItems)
                            currentItems.Add(item);
                    }
                });
        }

        public CustomNodeDesc<TInstance> AddListInput<T>(string name, Func<TInstance, T[]> getter, Action<TInstance, T[]> setter)
        {
            return AddInput<IReadOnlyList<T>>(name,
                getter: getter,
                equals: SequenceEqual,
                setter: (x, v) =>
                {
                    var newItems = v?.Where(i => i != null);
                    setter(x, newItems?.ToArray());
                });
        }

        public CustomNodeDesc<TInstance> AddOutput<T>(string name, Func<TInstance, T> getter)
        {
            outputs.Add(new CustomPinDesc()
            {
                Name = name.InsertSpaces(),
                Type = typeof(T),
                CreatePin = (node, instance) => new OutputPin<T>(node, instance, getter)
            });
            return this;
        }

        public CustomNodeDesc<TInstance> AddCachedOutput<T>(string name, Func<TInstance, T> getter)
        {
            outputs.Add(new CustomPinDesc()
            {
                Name = name.InsertSpaces(),
                Type = typeof(T),
                CreatePin = (node, instance) => new CachedOutputPin<T>(node, instance, getter)
            });
            return this;
        }

        public CustomNodeDesc<TInstance> AddCachedOutput<T>(string name, Func<NodeContext, TInstance, T> getter)
        {
            outputs.Add(new CustomPinDesc()
            {
                Name = name.InsertSpaces(),
                Type = typeof(T),
                CreatePin = (node, instance) => new CachedOutputPin<T>(node, instance, x => getter(node.Context, instance))
            });
            return this;
        }

        class CustomPinDesc : IVLPinDescription
        {
            public string Name { get; set; }

            public Type Type { get; set; }

            public object DefaultValue { get; set; }

            public Func<Node, TInstance, Pin> CreatePin { get; set; }
        }

        abstract class Pin : IVLPin
        {
            public readonly Node Node;
            public TInstance Instance;

            public Pin(Node node, TInstance instance)
            {
                Node = node;
                Instance = instance;
            }

            public abstract object BoxedValue { get; set; }

            // Update the pin by copying the underlying value to the new instance
            public virtual void Update(TInstance instance)
            {
                Instance = instance;
            }

            object IVLPin.Value
            {
                get => BoxedValue;
                set => BoxedValue = value;
            }
        }

        abstract class Pin<T> : Pin, IVLPin
        {
            public Func<TInstance, T> getter;
            public Action<TInstance, T> setter;

            public Pin(Node node, TInstance instance) : base(node, instance)
            {
            }

            public override sealed object BoxedValue 
            { 
                get => Value; 
                set => Value = (T)value; 
            }

            public abstract T Value { get; set; }
        }

        class InputPin<T> : Pin<T>, IVLPin
        {
            public readonly Func<T, T, bool> equals;

            public InputPin(Node node, TInstance instance, Func<TInstance, T> getter, Action<TInstance, T> setter, T initialValue, Func<T, T, bool> equals = default) 
                : base(node, instance)
            {
                this.equals = equals ?? EqualityComparer<T>.Default.Equals;
                this.getter = getter;
                this.setter = setter;
                this.InitialValue = initialValue;
                setter(instance, initialValue);
            }

            public T InitialValue { get; }

            public override T Value
            {
                get => getter(Instance);
                set
                {
                    if (!equals(value, lastValue))
                    {
                        lastValue = value;

                        // Normalize the value first
                        if (value is null)
                            value = InitialValue;

                        setter(Instance, value);
                        Node.needsUpdate = true;
                    }
                }
            }
            T lastValue;

            public override void Update(TInstance instance)
            {
                var currentValue = getter(Instance);
                base.Update(instance);
                setter(instance, currentValue);
            }
        }

        class OutputPin<T> : Pin<T>
        {
            public OutputPin(Node node, TInstance instance, Func<TInstance, T> getter) 
                : base(node, instance)
            {
                this.getter = getter;
            }

            public override T Value 
            {
                get
                {
                    if (Node.needsUpdate)
                        Node.Update();
                    return getter(Instance);
                }
                set => throw new InvalidOperationException(); 
            }
        }

        class CachedOutputPin<T> : OutputPin<T>
        {
            public CachedOutputPin(Node node, TInstance instance, Func<TInstance, T> getter)
                : base(node, instance, getter)
            {
                cachedValue = getter(instance);
            }

            public override T Value
            {
                get
                {
                    if (Node.needsUpdate)
                    {
                        Node.Update();
                        cachedValue = getter(Instance);
                    }
                    return cachedValue;
                }
                set => throw new InvalidOperationException();
            }
            T cachedValue;
        }

        class Node : VLObject, IVLNode
        {
            public Action updateAction;
            public Action disposeAction;
            public bool needsUpdate;

            public Node(NodeContext nodeContext) : base(nodeContext)
            {
            }

            public IVLNodeDescription NodeDescription { get; set; }

            public IVLPin[] Inputs { get; set; }

            public IVLPin[] Outputs { get; set; }

            public void Dispose() => disposeAction?.Invoke();

            public void Update() => updateAction?.Invoke();
        }
    }
}