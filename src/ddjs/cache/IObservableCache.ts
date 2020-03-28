import { IDisposable } from '../util/Disposable';
import { IConnectableCache } from './IConnectableCache';
import { IQuery } from './IQuery';

/**
 * A cache for observing and querying in memory data. With additional data access operatorsObservableCache
 */
export interface IObservableCache<TObject, TKey> extends IConnectableCache<TObject, TKey>, IQuery<TObject, TKey>, IDisposable {
    /**
     *  Gets the key associated with the object
     * @param item The item.
     */
    getKey(item: TObject): TKey;

    /**
     * The symbol name of the cache
     */
    readonly [Symbol.toStringTag]: 'ObservableCache';
}

export function isObservableCache(value: any): value is IObservableCache<any, any> {
    return value && value[Symbol.toStringTag] === 'ObservableCache';
}
