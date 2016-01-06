﻿using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace RxLite
{
    /// <summary>
    ///     ReactiveObject is the base object for ViewModel classes, and it
    ///     implements INotifyPropertyChanged. In addition, ReactiveObject provides
    ///     Changing and Changed Observables to monitor object changes.
    /// </summary>
    [DataContract]
    public class ReactiveObject : IReactiveNotifyPropertyChanged<IReactiveObject>, IHandleObservableErrors,
        IReactiveObject
    {
#if NET_45
        public event PropertyChangingEventHandler PropertyChanging;

        void IReactiveObject.RaisePropertyChanging(PropertyChangingEventArgs args)
        {
        var handler = this.PropertyChanging;
        if(handler != null) {
        handler(this, args);
        }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void IReactiveObject.RaisePropertyChanged(PropertyChangedEventArgs args)
        {
        var handler = this.PropertyChanged;
        if (handler != null) {
        handler(this, args);
        }
        }

#else
        public event PropertyChangingEventHandler PropertyChanging
        {
            add { PropertyChangingEventManager.AddHandler(this, value); }
            remove { PropertyChangingEventManager.RemoveHandler(this, value); }
        }

        void IReactiveObject.RaisePropertyChanging(PropertyChangingEventArgs args)
        {
            PropertyChangingEventManager.DeliverEvent(this, args);
        }

        public event PropertyChangedEventHandler PropertyChanged
        {
            add { PropertyChangedEventManager.AddHandler(this, value); }
            remove { PropertyChangedEventManager.RemoveHandler(this, value); }
        }

        void IReactiveObject.RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChangedEventManager.DeliverEvent(this, args);
        }
#endif

        /// <summary>
        ///     Represents an Observable that fires *before* a property is about to
        ///     be changed.
        /// </summary>
        [IgnoreDataMember]
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changing
            => ((IReactiveObject) this).GetChangingObservable();

        /// <summary>
        ///     Represents an Observable that fires *after* a property has changed.
        /// </summary>
        [IgnoreDataMember]
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changed
            => ((IReactiveObject) this).GetChangedObservable();

        /// <summary>
        /// </summary>
        [IgnoreDataMember]
        public IObservable<Exception> ThrownExceptions => this.GetThrownExceptionsObservable();

        protected ReactiveObject()
        {
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public IDisposable SuppressChangeNotifications()
        {
            return IReactiveObjectExtensions.SuppressChangeNotifications(this);
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public bool AreChangeNotificationsEnabled()
        {
            return IReactiveObjectExtensions.AreChangeNotificationsEnabled(this);
        }

        public IDisposable DelayChangeNotifications()
        {
            return IReactiveObjectExtensions.DelayChangeNotifications(this);
        }
    }
}