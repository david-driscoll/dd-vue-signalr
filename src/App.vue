<template>
    <v-app>
        <v-app-bar app color="primary" dark>
            <div class="d-flex align-center">
                <v-img
                        alt="Vuetify Logo"
                        class="shrink mr-2"
                        contain
                        src="https://cdn.vuetifyjs.com/images/logos/vuetify-logo-dark.png"
                        transition="scale-transition"
                        width="40"
                />

                <v-img
                        alt="Vuetify Name"
                        class="shrink mt-1 hidden-sm-and-down"
                        contain
                        min-width="100"
                        src="https://cdn.vuetifyjs.com/images/logos/vuetify-name-dark.png"
                        width="100"
                />
            </div>

            <v-spacer></v-spacer>

            <v-btn
                    href="https://github.com/vuetifyjs/vuetify/releases/latest"
                    target="_blank"
                    text
            >
                <span class="mr-2">Latest Release</span>
                <v-icon>fas fa-external-link-alt</v-icon>
            </v-btn>
        </v-app-bar>

        <v-content>
            <!--            <HelloWorld/>-->


            <v-row>
                <v-flex xs12 sm4>
                    <v-row v-for="[key, item] in people" :key="item.id">
                        <v-col>
                            {{item.firstName}} {{item.lastName}} ({{item.seed}}) [{{people.get(item.parentId) && people.get(item.parentId).name}}]
                        </v-col>
                    </v-row>
                </v-flex>
                <v-flex xs12 sm8>
                    <v-treeview :items="treeData" :open-all="true" item-key="id" item-text="name"
                                ref="tree">
                        <template v-slot:label="{ item }">
                            {{item.name}} ({{item.seed}})
                        </template>
                    </v-treeview>
                </v-flex>
            </v-row>

        </v-content>
    </v-app>
</template>

<script lang="ts">
    import Vue from 'vue'
    import * as signalR from "@microsoft/signalr";
    import {IChangeSet} from "@/ddjs/cache/IChangeSet";
    import {ConnectableObservable, Observable, from, Subscription, AsyncSubject} from "rxjs";
    import {bind} from "@/ddjs/cache/operators/bind";
    import {map, publish, publishReplay, refCount, shareReplay, switchAll, switchMap, tap} from "rxjs/operators";
    import {transformToTree} from "@/ddjs/cache/operators/transformToTree";
    import {transform} from "@/ddjs/cache/operators/transform";
    import {toArray, from as ixFrom} from 'ix/iterable';
    import {map as ixMap} from 'ix/iterable/operators';
    import {defineComponent, reactive, ref, onMounted, onUnmounted} from '@vue/composition-api'
    import {HubConnection} from "@microsoft/signalr";
    import {CompositeDisposable, IDisposableOrSubscription} from "@/ddjs/util";
    import {clone} from './ddjs/cache/operators/clone';
    import {asObservableCache} from "@/ddjs/cache/operators/asObservableCache";
    import {Node} from "@/ddjs/cache/Node";
    import {disposeMany} from "@/ddjs/cache/operators/disposeMany";
    import {toCollection} from "@/ddjs/cache/operators/toCollection";
    import {limitSizeTo} from "@/ddjs/cache/operators/limitSizeTo";
    import {expireAfter} from "@/ddjs/cache/operators/expireAfter";
    import {onItemRemoved} from "@/ddjs/cache/operators/onItemRemoved";

    interface Person {
        id: string;
        firstName: string;
        lastName: string;
        name: string;
        seed: number;
        parentId?: string;
    }

    interface PersonWithChildren {
        id: string;
        firstName: string;
        lastName: string;
        name: string;
        seed: number;
        parentId?: string;
        children: Person[];
    }

    const conn =
            new Observable<HubConnection>(observer => {
                const connection = new signalR.HubConnectionBuilder()
                        .withUrl("http://localhost:5000/data")
                        .build();

                return from(connection.start()).pipe(map(_ => connection)).subscribe(observer);
            }).pipe(shareReplay(1));

    function streamItem<T>(hub: Observable<HubConnection>, stream: string) {
        return new Observable<T>(observer => {

            const result = hub.pipe(
                    switchMap(hub => new Observable<T>(observer => {
                        const sub = hub.stream<T>(stream).subscribe(observer);
                        return () => {
                            sub.dispose();
                        };
                    }))
            );

            return result.subscribe(observer);
        }).pipe(publish(), refCount());
    }

    export default defineComponent({
        setup: function (props, context) {
            const disposable = new CompositeDisposable();
            onUnmounted(() => disposable.dispose());

            const tree = ref<any>();
            const treeData = ref([] as PersonWithChildren[]);
            const people = ref(new Map<string, Person>());

            const cache = asObservableCache(streamItem<IChangeSet<Person, string>>(conn, "people"), true)
            const people$ = cache.connect();

            function transformChildren(person: Node<Person, string>): PersonWithChildren & IDisposableOrSubscription {
                // const children = reactive(toArray(ixFrom(person.children.values()).pipe(ixMap(z => z.item))));
                const children = toArray(ixFrom(person.children.values()).pipe(ixMap(z => z.item)));
                const d = person.children.connect().pipe(
                        transform(transformChildren),
                        bind(children, bind.create(children, (v, k) => children.findIndex(x => x.id === k))),
                        tap(x => setTimeout(() => tree.value?.updateAll(true), 0)),
                ).subscribe();
                return {
                    ...person.item,
                    children,
                    dispose() {
                        d.unsubscribe();
                    }
                };
            }

            disposable.add(
                    people$
                            .pipe(
                                    onItemRemoved(x => tree.value?.updateAll(true)),
                                    transformToTree(z => z.parentId || ''),
                                    transform(transformChildren),
                                    disposeMany(),
                                    bind(treeData.value, bind.create(treeData.value, (v, k) => treeData.value.findIndex(x => x.id === k))),
                                    tap(x => setTimeout(() => tree.value?.updateAll(true), 0)),
                            )
                            .subscribe(),
                    people$
                            .pipe(
                                    limitSizeTo(10),
                                    clone(people.value),
                            ).subscribe(),
            );

            onMounted(() => {
                tree.value.updateAll(true)
            });

            return {tree, treeData, people};
        }
    })
</script>
