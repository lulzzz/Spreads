﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Spreads
{
    // TODO cache currentValue like in Zip

    /// <summary>
    /// Map cursor.
    /// </summary>
    public struct Map<TKey, TInput, TResult, TCursor> :
        ICursorSeries<TKey, TResult, Map<TKey, TInput, TResult, TCursor>>
        where TCursor : ISpecializedCursor<TKey, TInput, TCursor>
    {
        #region Cursor state

        // This region must contain all cursor state that is passed via constructor.
        // No additional state must be created.
        // All state elements should be assigned in Initialize and Clone methods
        // All inner cursors must be disposed in the Dispose method but references to them must be kept (they could be used as factories)
        // for re-initialization.

        internal Func<TKey, TInput, TResult> _selector;

        // NB must be mutable, could be a struct
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        internal TCursor _cursor;

        #endregion Cursor state

        #region Constructors

        internal Map(TCursor cursor, Func<TKey, TInput, TResult> selector) : this()
        {
            _selector = selector;
            _cursor = cursor;
        }

        internal Map(TCursor cursor, Func<TInput, TResult> selector) : this()
        {
            TResult GetValue(TKey key, TInput value)
            {
                return selector(value);
            }
            _selector = GetValue;
            _cursor = cursor;
        }

        #endregion Constructors

        #region Lifetime management

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Map<TKey, TInput, TResult, TCursor> Clone()
        {
            var instance = new Map<TKey, TInput, TResult, TCursor>
            {
                _cursor = _cursor.Clone(),
                _selector = _selector,
            };
            return instance;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Map<TKey, TInput, TResult, TCursor> Initialize()
        {
            var instance = new Map<TKey, TInput, TResult, TCursor>
            {
                _cursor = _cursor.Initialize(),
                _selector = _selector,
            };
            return instance;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            // NB keep cursor state for reuse
            // dispose is called on the result of Initialize(), the cursor from
            // constructor could be uninitialized but contain some state, e.g. _value for this ArithmeticCursor
            _cursor.Dispose();
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _cursor.Reset();
        }

        ICursor<TKey, TResult> ICursor<TKey, TResult>.Clone()
        {
            return Clone();
        }

        #endregion Lifetime management

        #region ICursor members

        /// <inheritdoc />
        public KeyValuePair<TKey, TResult> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new KeyValuePair<TKey, TResult>(CurrentKey, CurrentValue); }
        }

        /// <inheritdoc />
        public TKey CurrentKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _cursor.CurrentKey; }
        }

        /// <inheritdoc />
        public TResult CurrentValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _selector(_cursor.CurrentKey, _cursor.CurrentValue);
            }
        }

        /// <inheritdoc />
        public IReadOnlySeries<TKey, TResult> CurrentBatch => throw new NotSupportedException();

        /// <inheritdoc />
        public KeyComparer<TKey> Comparer => _cursor.Comparer;

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool IsContinuous => _cursor.IsContinuous;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(TKey key, out TResult value)
        {
            if (_cursor.TryGetValue(key, out var v))
            {
                value = _selector(key, v);
                return true;
            }
            value = default(TResult);
            return false;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveAt(TKey key, Lookup direction)
        {
            var moved = _cursor.MoveAt(key, direction);
            return moved;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)] // NB NoInlining is important to speed-up MoveNext
        public bool MoveFirst()
        {
            var moved = _cursor.MoveFirst();
            return moved;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)] // NB NoInlining is important to speed-up MovePrevious
        public bool MoveLast()
        {
            var moved = _cursor.MoveLast();
            return moved;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return _cursor.MoveNext();
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<bool> MoveNextBatch(CancellationToken cancellationToken)
        {
            return Utils.TaskUtil.FalseTask;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MovePrevious()
        {
            return _cursor.MovePrevious();
        }

        /// <inheritdoc />
        IReadOnlySeries<TKey, TResult> ICursor<TKey, TResult>.Source => new Series<TKey, TResult, Map<TKey, TInput, TResult, TCursor>>(this);

        /// <summary>
        /// Get a <see cref="Series{TKey,TValue,TCursor}"/> based on this cursor.
        /// </summary>
        public Series<TKey, TResult, Map<TKey, TInput, TResult, TCursor>> Source => new Series<TKey, TResult, Map<TKey, TInput, TResult, TCursor>>(this);

        /// <inheritdoc />
        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        #endregion ICursor members

        #region Custom Properties

        /// <summary>
        /// A value used by TOp.
        /// </summary>
        public Func<TKey, TInput, TResult> Selector => _selector;

        #endregion Custom Properties

        #region ICursorSeries members

        /// <inheritdoc />
        public bool IsIndexed => _cursor.Source.IsIndexed;

        /// <inheritdoc />
        public bool IsReadOnly
        {
            // NB this property is repeatedly called from MNA
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _cursor.Source.IsReadOnly; }
        }

        /// <inheritdoc />
        public Task<bool> Updated
        {
            // NB this property is repeatedly called from MNA
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _cursor.Source.Updated; }
        }

        #endregion ICursorSeries members
    }
}