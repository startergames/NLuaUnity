using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using KeraLua;
using NLua.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Profiling;
using Lua = NLua.Lua;
using LuaFunction = NLua.LuaFunction;

namespace NLuaUnity {
    public class LuaExecutor : MonoBehaviour {
        [System.Serializable]
        public class BindObject {
            public string name;
            public UnityEngine.Object reference;
        }

        [TextArea(3, 50)] public string script;
        public TextAsset[] files;
        public BindObject[] BindObjects;

        private static Lua lua;
        private LuaFunction start;
        private LuaFunction onEnable;
        private LuaFunction onDisable;
        private LuaFunction update;

        private Dictionary<object, object> luaField = new Dictionary<object, object>();

        public object this[object i] {
            get => luaField.TryGetValue(i, out var result) ? result : null;
            set {
                Debug.Log($"{gameObject.name}: {i} = {value}");
                luaField[i] = value;
            }
        }

        void Awake() {
            Profiler.BeginSample("Set LuaState");
            if (lua == null) {
                lua = new Lua();
                lua.State.Encoding = Encoding.UTF8;
                lua.LoadCLRPackage();
                lua.DoString("import 'UnityEngine'");
            }

            // foreach (var r in BindObjects) {
            //     lua[$"this.{r.name}"] = r.reference;
            // }

            lua.Push(this);
            foreach (var r in BindObjects) {
                lua.Push(r.reference);
                lua.State.SetField(-2, r.name);
            }

            lua.Pop();

            foreach (var f in files) {
                DoString(f.text);
            }

            DoString(script);

            Profiler.EndSample();
        }

        private void OnDestroy() {
            PCallMember("OnDestroy");
        }

        [Button]
        private void DoString(string str) {
            var oldTop = lua.State.GetTop();
            if (lua.State.LoadString(str) != LuaStatus.OK) {
                lua.State.SetTop(oldTop);
                return;
            }

            StartCoroutine(CoLuaCoroutine(this));
            //localState.DoString(script);
        }

        private void PCallMember(string name, params object[] param) {
            lua.Push(this);
            lua.State.GetField(-1, name);
            if (lua.State.IsNil(-1)) {
                lua.State.Pop(2);
                return;
            }
            lua.State.Insert(-2);
            foreach (var p in param) {
                lua.Push(p);
            }

            var result = lua.State.PCall(param.Length + 1, 0, 0);
            if (result != LuaStatus.OK) {
                var err = lua.State.ToString(-1);
                Debug.LogError(err);
                lua.State.Pop(1);
            }
        }

        private IEnumerator CoLuaCoroutine(object self = null, params object[] param) {
            var thread = lua.NewThread();
            if (self != null) {
                thread["this"] = self;
            }

            var refId = lua.State.Ref(LuaRegistry.Index);

            // 스택에 쌓여 있는 함수를 코루틴으로 옮김
            // member일 경우에는 self가 필요하기 때문에 객체도 함께 옮김.
            lua.State.XMove(thread.State, 1);

            thread.Push(self);
            // 함수 인자들을 스택에 푸시
            foreach (var p in param) {
                thread.Push(p);
            }

            var arguments = self != null ? param.Length + 1 : param.Length;
            while (true) {
                var status = thread.State.Resume(thread.State, arguments, out var results);
                arguments = 0;
                //var status = localState.State.PCall(0, -1, 0);
                if (status == LuaStatus.Yield) {
                    var seconds = thread.State.ToNumber(-1);
                    thread.State.Pop(results);
                    yield return new WaitForSeconds((float)seconds);
                }
                else if (status == LuaStatus.OK) {
                    thread.State.Pop(results);
                    break;
                }
                else {
                    var errorStr = thread.State.ToString(-1);
                    Debug.LogError(errorStr);
                    thread.State.Pop(1);
                    break;
                }
            }

            lua.State.Unref(refId);
            thread.Dispose();
        }

        private void CallLuaMemberFuction(string name, params object[] param) {
            lua.Push(this);
            lua.State.GetField(-1, name);
            if (lua.State.IsNil(-1)) {
                lua.State.Pop(2);
                return;
            }

            lua.State.Remove(-2);
            StartCoroutine(CoLuaCoroutine(this, param));
        }

        private void Start() {
            CallLuaMemberFuction("Start");
        }

        private void OnEnable() {
            CallLuaMemberFuction("OnEnalbe");
        }

        private void OnDisable() {
            // Coroutine이 실행될 수 없으므로 즉시 호출
            PCallMember("OnDisable");
        }

        private void Update() {
            CallLuaMemberFuction("Update");
        }
    }
}