﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;

namespace RxLite
{
#if !NET_45
    public class CanExecuteChangedEventManager : WeakEventManager<ICommand, EventHandler, EventArgs>
    {
    }

    public class PropertyChangingEventManager :
        WeakEventManager<INotifyPropertyChanging, PropertyChangingEventHandler, PropertyChangingEventArgs>
    {
    }

    public class PropertyChangedEventManager :
        WeakEventManager<INotifyPropertyChanged, PropertyChangedEventHandler, PropertyChangedEventArgs>
    {
    }

    public class CollectionChangingEventManager :
        WeakEventManager
            <INotifyCollectionChanging, NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>
    {
    }

    public class CollectionChangedEventManager :
        WeakEventManager
            <INotifyCollectionChanged, NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>
    {
    }
#endif

    /// <summary>
    ///     WeakEventManager base class. Inspired by the WPF WeakEventManager class and the code in
    ///     http://social.msdn.microsoft.com/Forums/silverlight/en-US/34d85c3f-52ea-4adc-bb32-8297f5549042/command-binding-memory-leak?forum=silverlightbugs
    /// </summary>
    /// <typeparam name="TEventSource">The type of the event source.</typeparam>
    /// <typeparam name="TEventHandler">The type of the event handler.</typeparam>
    /// <typeparam name="TEventArgs">The type of the event arguments.</typeparam>
    public class WeakEventManager<TEventSource, TEventHandler, TEventArgs>
    {
        private static readonly object StaticSource = new object();

        private static readonly Lazy<WeakEventManager<TEventSource, TEventHandler, TEventArgs>> _current =
            new Lazy<WeakEventManager<TEventSource, TEventHandler, TEventArgs>>(
                () => new WeakEventManager<TEventSource, TEventHandler, TEventArgs>());

        /// <summary>
        ///     Mapping from the source of the event to the list of handlers. This is a CWT to ensure it does not leak the source
        ///     of the event.
        /// </summary>
        private readonly ConditionalWeakTable<object, WeakHandlerList> _sourceToWeakHandlers =
            new ConditionalWeakTable<object, WeakHandlerList>();

        /// <summary>
        ///     Mapping between the target of the delegate (for example a Button) and the handler (EventHandler).
        ///     Windows Phone needs this, otherwise the event handler gets garbage collected.
        /// </summary>
        private readonly ConditionalWeakTable<object, List<Delegate>> _targetToEventHandler =
            new ConditionalWeakTable<object, List<Delegate>>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="WeakEventManager{TEventSource, TEventHandler, TEventArgs}" /> class.
        ///     Protected to disallow instances of this class and force a subclass.
        /// </summary>
        protected WeakEventManager()
        {
        }

        private static WeakEventManager<TEventSource, TEventHandler, TEventArgs> Current => _current.Value;

        /// <summary>
        ///     Adds a weak reference to the handler and associates it with the source.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="handler">The handler.</param>
        public static void AddHandler(TEventSource source, TEventHandler handler)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (handler == null)
                throw new ArgumentNullException("handler");

            if (!typeof (TEventHandler).GetTypeInfo().IsSubclassOf(typeof (Delegate)))
            {
                throw new ArgumentException("Handler must be Delegate type");
            }

            Current.PrivateAddHandler(source, handler);
        }

        /// <summary>
        ///     Removes the association between the source and the handler.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="handler">The handler.</param>
        public static void RemoveHandler(TEventSource source, TEventHandler handler)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (handler == null)
                throw new ArgumentNullException("handler");

            if (!typeof (TEventHandler).GetTypeInfo().IsSubclassOf(typeof (Delegate)))
            {
                throw new ArgumentException("handler must be Delegate type");
            }

            Current.PrivateRemoveHandler(source, handler);
        }

        /// <summary>
        ///     Delivers the event to the handlers registered for the source.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="TEventArgs" /> instance containing the event data.</param>
        public static void DeliverEvent(TEventSource sender, TEventArgs args)
        {
            Current.PrivateDeliverEvent(sender, args);
        }

        /// <summary>
        ///     Override this method to attach to an event.
        /// </summary>
        /// <param name="source">The source.</param>
        protected virtual void StartListening(object source)
        {
        }

        /// <summary>
        ///     Override this method to detach from an event.
        /// </summary>
        /// <param name="source">The source.</param>
        protected virtual void StopListening(object source)
        {
        }

        private void PrivateAddHandler(TEventSource source, TEventHandler handler)
        {
            AddWeakHandler(source, handler);
            AddTargetHandler(handler);
        }

        private void AddWeakHandler(TEventSource source, TEventHandler handler)
        {
            WeakHandlerList weakHandlers;
            if (_sourceToWeakHandlers.TryGetValue(source, out weakHandlers))
            {
                // clone list if we are currently delivering an event
                if (weakHandlers.IsDeliverActive)
                {
                    weakHandlers = weakHandlers.Clone();
                    _sourceToWeakHandlers.Remove(source);
                    _sourceToWeakHandlers.Add(source, weakHandlers);
                }
                weakHandlers.AddWeakHandler(source, handler);
            }
            else
            {
                weakHandlers = new WeakHandlerList();
                weakHandlers.AddWeakHandler(source, handler);

                _sourceToWeakHandlers.Add(source, weakHandlers);
                StartListening(source);
            }

            Purge(source);
        }

        private void AddTargetHandler(TEventHandler handler)
        {
            var @delegate = handler as Delegate;
            var key = @delegate.Target ?? StaticSource;
            List<Delegate> delegates;

            if (_targetToEventHandler.TryGetValue(key, out delegates))
            {
                delegates.Add(@delegate);
            }
            else
            {
                delegates = new List<Delegate> {@delegate};

                _targetToEventHandler.Add(key, delegates);
            }
        }

        private void PrivateRemoveHandler(TEventSource source, TEventHandler handler)
        {
            RemoveWeakHandler(source, handler);
            RemoveTargetHandler(handler);
        }

        private void RemoveWeakHandler(TEventSource source, TEventHandler handler)
        {
            WeakHandlerList weakHandlers;

            if (_sourceToWeakHandlers.TryGetValue(source, out weakHandlers))
            {
                // clone list if we are currently delivering an event
                if (weakHandlers.IsDeliverActive)
                {
                    weakHandlers = weakHandlers.Clone();
                    _sourceToWeakHandlers.Remove(source);
                    _sourceToWeakHandlers.Add(source, weakHandlers);
                }

                if (weakHandlers.RemoveWeakHandler(source, handler) && weakHandlers.Count == 0)
                {
                    _sourceToWeakHandlers.Remove(source);
                    StopListening(source);
                }
            }
        }

        private void RemoveTargetHandler(TEventHandler handler)
        {
            var @delegate = handler as Delegate;
            var key = @delegate.Target ?? StaticSource;

            var delegates = default(List<Delegate>);
            if (_targetToEventHandler.TryGetValue(key, out delegates))
            {
                delegates.Remove(@delegate);

                if (delegates.Count == 0)
                {
                    _targetToEventHandler.Remove(key);
                }
            }
        }

        private void PrivateDeliverEvent(object sender, TEventArgs args)
        {
            var source = sender ?? StaticSource;
            var weakHandlers = default(WeakHandlerList);

            var hasStaleEntries = false;

            if (_sourceToWeakHandlers.TryGetValue(source, out weakHandlers))
            {
                using (weakHandlers.DeliverActive())
                {
                    hasStaleEntries = weakHandlers.DeliverEvent(source, args);
                }
            }

            if (hasStaleEntries)
            {
                Purge(source);
            }
        }

        private void Purge(object source)
        {
            var weakHandlers = default(WeakHandlerList);

            if (_sourceToWeakHandlers.TryGetValue(source, out weakHandlers))
            {
                if (weakHandlers.IsDeliverActive)
                {
                    weakHandlers = weakHandlers.Clone();
                    _sourceToWeakHandlers.Remove(source);
                    _sourceToWeakHandlers.Add(source, weakHandlers);
                }
                else
                {
                    weakHandlers.Purge();
                }
            }
        }

        private class WeakHandler
        {
            private readonly WeakReference _originalHandler;
            private readonly WeakReference _source;

            public WeakHandler(object source, TEventHandler originalHandler)
            {
                this._source = new WeakReference(source);
                this._originalHandler = new WeakReference(originalHandler);
            }

            public bool IsActive => _source != null && _source.IsAlive && _originalHandler != null && _originalHandler.IsAlive;

            public TEventHandler Handler
            {
                get
                {
                    if (_originalHandler == null)
                    {
                        return default(TEventHandler);
                    }
                    return (TEventHandler) _originalHandler.Target;
                }
            }

            public bool Matches(object source, TEventHandler handler)
            {
                return this._source != null &&
                       ReferenceEquals(this._source.Target, source) &&
                       _originalHandler != null &&
                       (ReferenceEquals(_originalHandler.Target, handler) ||
                        (_originalHandler.Target is PropertyChangedEventHandler &&
                         handler is PropertyChangedEventHandler &&
                         Equals((_originalHandler.Target as PropertyChangedEventHandler).Target,
                             (handler as PropertyChangedEventHandler).Target)));
            }
        }

        internal class WeakHandlerList
        {
            private int _deliveries;
            private readonly List<WeakHandler> _handlers;

            public WeakHandlerList()
            {
                _handlers = new List<WeakHandler>();
            }

            public int Count => _handlers.Count;

            public bool IsDeliverActive => _deliveries > 0;

            public void AddWeakHandler(TEventSource source, TEventHandler handler)
            {
                var handlerSink = new WeakHandler(source, handler);
                _handlers.Add(handlerSink);
            }

            public bool RemoveWeakHandler(TEventSource source, TEventHandler handler)
            {
                foreach (var weakHandler in _handlers)
                {
                    if (weakHandler.Matches(source, handler))
                    {
                        return _handlers.Remove(weakHandler);
                    }
                }

                return false;
            }

            public WeakHandlerList Clone()
            {
                var newList = new WeakHandlerList();
                newList._handlers.AddRange(_handlers.Where(h => h.IsActive));

                return newList;
            }

            public IDisposable DeliverActive()
            {
                Interlocked.Increment(ref _deliveries);

                return Disposable.Create(() => Interlocked.Decrement(ref _deliveries));
            }

            public virtual bool DeliverEvent(object sender, TEventArgs args)
            {
                var hasStaleEntries = false;

                foreach (var handler in _handlers)
                {
                    if (handler.IsActive)
                    {
                        var @delegate = handler.Handler as Delegate;
                        @delegate.DynamicInvoke(sender, args);
                    }
                    else
                    {
                        hasStaleEntries = true;
                    }
                }

                return hasStaleEntries;
            }

            public void Purge()
            {
                for (var i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (!_handlers[i].IsActive)
                    {
                        _handlers.RemoveAt(i);
                    }
                }
            }
        }
    }
}