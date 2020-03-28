import { NotifyPropertyChangedType } from '../../notify/notifyPropertyChangedSymbol';
import { MonoTypeOperatorFunction, OperatorFunction, SchedulerLike } from 'rxjs';
import { IChangeSet } from '../IChangeSet';
import { autoRefreshOnObservable } from './autoRefreshOnObservable';
import { whenAnyPropertyChanged } from './whenAnyPropertyChanged';
import { throttleTime } from 'rxjs/operators';
import { ChangeSetOperatorFunction } from '../ChangeSetOperatorFunction';

/**
 * Automatically refresh downstream operators when any properties change.
 * @param changeSetBuffer Batch up changes by specifying the buffer. This greatly increases performance when many elements have sucessive property changes
 * @param propertyChangeThrottle When observing on multiple property changes, apply a throttle to prevent excessive refesh invocations
 * @param scheduler The scheduler
 */
export function autoRefresh<TObject, TKey>(
    changeSetBuffer?: number,
    propertyChangeThrottle?: number,
    scheduler?: SchedulerLike,
): ChangeSetOperatorFunction<TObject, TKey, NotifyPropertyChangedType<TObject>>;
/**
 * Automatically refresh downstream operators when any properties change.
 * @param key the property to watch
 * @param changeSetBuffer Batch up changes by specifying the buffer. This greatly increases performance when many elements have sucessive property changes
 * @param propertyChangeThrottle When observing on multiple property changes, apply a throttle to prevent excessive refesh invocations
 * @param scheduler The scheduler
 */
export function autoRefresh<TObject, TKey>(
    key: keyof TObject,
    changeSetBuffer?: number,
    propertyChangeThrottle?: number,
    scheduler?: SchedulerLike,
): ChangeSetOperatorFunction<TObject, TKey, NotifyPropertyChangedType<TObject>>;
export function autoRefresh<TObject, TKey>(
    key?: number | keyof TObject,
    changeSetBuffer?: number,
    propertyChangeThrottle?: number | SchedulerLike,
    scheduler?: SchedulerLike,
): ChangeSetOperatorFunction<TObject, TKey, NotifyPropertyChangedType<TObject>> {
    let props: string[] = [];
    if (typeof key === 'string' || typeof key === 'symbol') {
        props.push(key as any);
    } else {
        scheduler = propertyChangeThrottle as any;
        propertyChangeThrottle = changeSetBuffer as any;
        changeSetBuffer = key as any;
    }

    return function autoRefreshOperator(source) {
        return source.pipe(
            autoRefreshOnObservable<TObject, TKey>((t, v) => {
                if (propertyChangeThrottle) {
                    return whenAnyPropertyChanged(t, ...props as any[])
                        .pipe(throttleTime(propertyChangeThrottle as number, scheduler));
                } else {
                    return whenAnyPropertyChanged(t, ...props as any[]);
                }
            }, changeSetBuffer as number | undefined, scheduler),
        );
    };
}