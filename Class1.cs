using BepInEx;
using HarmonyLib;
using HumanAPI;
using Multiplayer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TASMod
{
    public struct Body
    {
        public Transform transform;
        public Rigidbody rigidbody;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
    }

    public class SaveState
    {
        public int frame;
        public Dictionary<uint, NetBodyState> bodies;
        public Dictionary<int, HumanSaveState> humans;
        public bool passed;
    }

    public struct GrabState
    {
        public int grabState;
        public GameObject grabObject;
        public Vector3 anchor;
        public Rigidbody grabBody;
        public Rigidbody connectedBody;
        public Vector3 connectedAnchor;
    }

    public class HumanSaveState
    {
        public Body[] bodies = new Body[50];
        public HumanState humanState;
        public float cameraPitchAngle;
        public float cameraYawAngle;
        public List<GameObject> grabbedObjects = new List<GameObject>();
        public float unconsciousTime;
        public Vector3 grabStartPosition;
        public float fallTimer;
        public float groundDelay;
        public float jumpDelay;
        public float slideTimer;
        public float leftBlockTime;
        public float rightBlockTime;
        public GrabState leftGrabState;
        public GrabState rightGrabState;
        public float walkSpeed;
        public float diveTime;
    }

    public struct NetBodyState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
    }

    public static class PFUtil
    {
        public static void InitFields()
        {
            Type human = typeof(Human);
            Type collisionSensor = typeof(CollisionSensor);
            Type humanHead = typeof(HumanHead);
            Type signalManager = typeof(SignalManager);
            Type humanControls = typeof(HumanControls);

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            grabManager = human.GetField("grabManager", flags);
            grabStartPosition = human.GetField("grabStartPosition", flags);
            fallTimer = human.GetField("fallTimer", flags);
            groundDelay = human.GetField("groundDelay", flags);
            jumpDelay = human.GetField("jumpDelay", flags);
            slideTimer = human.GetField("slideTimer", flags);
            blockGrab = collisionSensor.GetField("blockGrab", flags);
            diveTime = humanHead.GetField("diveTime", flags);
            groundAngles = human.GetField("groundAngles", flags);
            groundAnglesSum = human.GetField("groundAnglesSum", flags);
            lastGroundAngle = human.GetField("lastGroundAngle", flags);
            humanScript = humanControls.GetField("humanScript", flags);

            beginReset = signalManager.GetMethod("BeginReset", BindingFlags.Static | BindingFlags.NonPublic);
        }

        public static FieldInfo grabManager;
        public static FieldInfo grabStartPosition;
        public static FieldInfo fallTimer;
        public static FieldInfo groundDelay;
        public static FieldInfo jumpDelay;
        public static FieldInfo slideTimer;
        public static FieldInfo blockGrab;
        public static FieldInfo diveTime;
        public static FieldInfo groundAngles;
        public static FieldInfo groundAnglesSum;
        public static FieldInfo lastGroundAngle;
        public static FieldInfo humanScript;

        public static MethodInfo beginReset;
    }

    [BepInPlugin("org.bepinex.plugins.tasmod", "TAS Mod", "1.6.7.1")]
    [BepInProcess("Human.exe")]
    public class TASMod : BaseUnityPlugin
    {
        public void Start()
        {
            currentFrames = new List<SaveState>();
            saveFrames = new List<SaveState>();
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            color = new Color32(255, 128, 0, 255);
            windowRect = new Rect(10, 400, 200, 280);
            freezeRect = new Rect(Screen.width - 210, 410, 200, 200);
            PFUtil.InitFields();
            Harmony.CreateAndPatchAll(typeof(TASMod));
        }

        public void Update()
        {
            if (Game.instance == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                guiOpened = !guiOpened;
            }
            if (!tasMode)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SaveGameState();
            }
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                LoadGameState();
            }
            if (Input.GetKeyDown(KeyCode.X))
            {
                StartCoroutine(FrameAdvance());
            }
            if (Input.GetKeyDown(KeyCode.U))
            {
                float[] groundAngles = (float[])PFUtil.groundAngles.GetValue(Human.Localplayer);
                for (int i = 0; i < groundAngles.Length; i++)
                {
                    groundAngles[i] = 0;
                }
                PFUtil.groundAnglesSum.SetValue(Human.Localplayer, 0);
                Human.Localplayer.groundAngle = 0;
                PFUtil.lastGroundAngle.SetValue(Human.Localplayer, 0);
            }
            if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                int checkpointNumber = Game.instance.currentCheckpointNumber;
                int subObjectives = Game.instance.currentCheckpointSubObjectives;
                PFUtil.beginReset.Invoke(null, null);
                Game.currentLevel.prerespawn?.Invoke(checkpointNumber, false);
                Game.currentLevel.Reset(checkpointNumber, subObjectives);
                if (NetGame.isServer)
                {
                    NetSceneManager.ResetLevel(checkpointNumber, subObjectives);
                }
                Game.currentLevel.BeginLevel();
                Game.instance.CheckpointLoaded(checkpointNumber);
                SignalManager.EndReset();
                Game.currentLevel.PostEndReset(checkpointNumber);
            }
            if (Input.GetKeyDown(KeyCode.Space) && readonlyMode)
            {
                playing = !playing;
            }
            if (Input.GetKeyDown(KeyCode.G) && freeze)
            {
                if (Human.Localplayer.hasGrabbed)
                {
                    GameObject object_ = ((GrabManager)PFUtil.grabManager.GetValue(Human.Localplayer)).grabbedObjects[0];
                    NetIdentity identity = object_.GetComponent<NetIdentity>();
                    if (identity != null)
                    {
                        selectedObject = identity.sceneId;
                        frozenObjects.Add(identity.sceneId);
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.Alpha8) && Input.GetKey(KeyCode.LeftShift) && MenuSystem.keyboardState == KeyboardState.None)
            {
                readonlyMode = !readonlyMode;
                if (readonlyMode)
                {
                    Physics.autoSimulation = false;
                    Time.timeScale = 1;
                    playing = false;
                    return;
                }
                Physics.autoSimulation = true;
                Time.timeScale = speed;
                currentFrames.RemoveRange(currentFrame, currentFrames.Count - currentFrame);
            }
        }

        public void FixedUpdate()
        {
            if (!tasMode)
            {
                return;
            }
            if (Game.instance.state == GameState.PlayingLevel && !readonlyMode)
            {
                currentFrame++;
                SaveFrame();
                currentFrames.Add(saveState);
            }
            if (oldState == GameState.LoadingLevel && Game.instance.state == GameState.PlayingLevel)
            {
                currentFrame = 0;
                currentFrames.Clear();
            }
            if (readonlyMode)
            {
                int num = (Input.GetKey(KeyCode.RightArrow) || playing) ? 1 : (Input.GetKey(KeyCode.LeftArrow) ? -1 : 0);
                currentFrame += num * (Input.GetKey(KeyCode.LeftShift) ? 10 : 1);
                currentFrame = Mathf.Clamp(currentFrame, 0, currentFrames.Count - 1);
                saveState = currentFrames[currentFrame];
                LoadFrame();
            }
            oldState = Game.instance.state;
        }

        public static string TimeFormat(float time)
        {
            int num = (int)time;
            int num2 = num / 3600;
            int num3 = num % 3600 / 60;
            float num4 = time - num2 * 3600 - (num3 * 60);
            if (num < 60)
            {
                return string.Format("{0:0.00}", num4);
            }
            if (num < 3600)
            {
                return string.Format("{0}:{1:00.00}", num3, num4);
            }
            return string.Format("{0}:{1:00}:{2:00.00}", num2, num3, num4);
        }

        public void OnGUI()
        {
            GUIStyle guistyle = new GUIStyle
            {
                fontSize = 30
            };
            guistyle.normal.textColor = color;
            GUIStyle guistyle2 = new GUIStyle
            {
                fontSize = 30
            };
            guistyle2.normal.textColor = Color.blue;
            GUIStyle guistyleGreen = new GUIStyle
            {
                fontSize = 30
            };
            guistyleGreen.normal.textColor = Color.green;
            GUIStyle guistyleRed = new GUIStyle
            {
                fontSize = 30
            };
            guistyleRed.normal.textColor = Color.red;
            if (guiOpened)
            {
                windowRect = GUI.Window(200, windowRect, WindowFunction, "TAS Mod " + modVersion);
            }
            if (freeze)
            {
                freezeRect = GUI.Window(201, freezeRect, WindowFunction, "Freeze Object");
            }
            if (debug)
            {
                GUI.Label(new Rect(Screen.width - 600, Screen.height - 110, 600, 30), string.Format("State: {0} GrabObject: {1} {2}",
                    Human.Localplayer.state, Human.Localplayer.ragdoll.partLeftHand.sensor.grabObject != null, Human.Localplayer.ragdoll.partRightHand.sensor.grabObject != null), guistyle2);
                GUI.Label(new Rect(Screen.width - 600, Screen.height - 80, 600, 30), string.Format("Passed: {0}", Game.instance.passedLevel), guistyle2);
                GUI.Label(new Rect(Screen.width - 600, Screen.height - 50, 600, 30), string.Format("Pos: {0}", Human.Localplayer.transform.position), guistyle2);
            }
            if (tasMode)
            {
                GUI.Label(new Rect(20, 20, 150, 30), "TASMod " + modVersion, guistyle);
                GUI.Label(new Rect(20, 50, 150, 30), readonlyMode ? "Playback" : "Recording", guistyle);
                GUI.Label(new Rect(20, 80, 150, 30), string.Format("{0} ({1})", currentFrame, TimeFormat(currentFrame / 60f)), guistyle);
                GUI.Label(new Rect(20, 110, 150, 30), string.Format("Progress: {0}", fileProgress == 5 ? "File already exists!" : ((fileProgress == 4) ? "Too old version!" : ((fileProgress == 3) ? "Complete" : (Math.Round(fileProgress * 100, 1).ToString() + "%")))), guistyle);
            }
        }

        public void WindowFunction(int windowID)
        {
            switch (windowID)
            {
                case 200:
                    tasMode = GUI.Toggle(new Rect(10, 30, 110, 20), tasMode, "TAS Mode");
                    debug = GUI.Toggle(new Rect(120, 30, 70, 20), debug, "Debug");
                    autoGetUp = GUI.Toggle(new Rect(10, 50, 110, 20), autoGetUp, "Auto Get-Up");
                    freeze = GUI.Toggle(new Rect(120, 50, 70, 20), freeze, "Freeze");
                    saveHand = GUI.Toggle(new Rect(10, 70, 110, 20), saveHand, "Save Hand");
                    modifySpawn = GUI.Toggle(new Rect(10, 90, 110, 20), modifySpawn, "Modify Spawn");
                    curSpawn = GUI.TextField(new Rect(120, 90, 70, 20), curSpawn);

                    GUI.Label(new Rect(10, 120, 180, 20), "File Name:");
                    fileName = GUI.TextField(new Rect(10, 140, 180, 20), fileName);
                    if (GUI.Button(new Rect(10, 170, 90, 30), "Save"))
                    {
                        StartCoroutine(SaveFile(fileName));
                    }
                    if (GUI.Button(new Rect(100, 170, 90, 30), "Load"))
                    {
                        StartCoroutine(LoadFile(fileName));
                    }
                    if (GUI.Button(new Rect(10, 210, 50, 20), "Speed") && float.TryParse(curSpeed, out float num))
                    {
                        speed = num;
                    }
                    curSpeed = GUI.TextField(new Rect(70, 210, 120, 20), curSpeed);
                    if (GUI.Button(new Rect(10, 240, 180, 30), "Quit"))
                    {
                        Destroy(this);
                    }
                    break;
                case 201:
                    GUIStyle styleGreen = new GUIStyle();
                    styleGreen.normal.textColor = Color.green;
                    GUIStyle styleWhite = new GUIStyle();
                    styleWhite.normal.textColor = Color.white;
                    GUIStyle styleRed = new GUIStyle();
                    styleRed.normal.textColor = Color.red;

                    NetIdentity[] identities = FindObjectsOfType<NetIdentity>();
                    for (int i = 0; i < frozenObjects.Count; i++)
                    {
                        NetIdentity identity = identities.First((idty) => idty.sceneId == frozenObjects[i]);
                        if (GUI.Button(new Rect(10, 30 + i * 30, 150, 30), identity == null ? frozenObjects[i].ToString() : identity.gameObject.name, frozenObjects[i] == selectedObject ? styleGreen : styleWhite))
                        {
                            selectedObject = frozenObjects[i];
                        }
                        if (GUI.Button(new Rect(160, 30 + i * 30, 30, 30), "×", styleRed))
                        {
                            frozenObjects.RemoveAt(i);
                        }
                    }
                    curFreeze = GUI.TextField(new Rect(100, 170, 90, 20), curFreeze);
                    if (GUI.Button(new Rect(10, 170, 90, 20), "Freeze Until") && readonlyMode)
                    {
                        if (uint.TryParse(curFreeze, out uint freezeUntil) && freezeUntil > currentFrame)
                        {
                            NetBodyState state = currentFrames[currentFrame].bodies[selectedObject];
                            for (int i = currentFrame; i < freezeUntil; i++)
                            {
                                currentFrames[i].bodies[selectedObject] = state;
                            }
                        }
                    }
                    break;
            }
            GUI.DragWindow(new Rect(0, 0, Screen.width, Screen.height));
        }

        public void SaveFrame()
        {
            saveState = new SaveState
            {
                humans = new Dictionary<int, HumanSaveState>(),
                bodies = new Dictionary<uint, NetBodyState>()
            };
            for (int i = 0; i < Human.all.Count; i++)
            {
                HumanSaveState humanSaveState = new HumanSaveState();
                SaveHuman(Human.all[i], humanSaveState);
                saveState.humans[i] = humanSaveState;
            }
            foreach (NetBody netBody in FindObjectsOfType<NetBody>())
            {
                if (netBody.gameObject.scene.name != "DontDestroyOnLoad")
                {
                    Rigidbody component = netBody.gameObject.GetComponent<Rigidbody>();
                    saveState.bodies[netBody.gameObject.GetComponent<NetIdentity>().sceneId] = new NetBodyState
                    {
                        position = netBody.transform.position,
                        rotation = netBody.transform.rotation,
                        velocity = component.velocity,
                        angularVelocity = component.angularVelocity
                    };
                }
            }
            saveState.frame = currentFrame;
            saveState.passed = Game.instance.passedLevel;
        }

        public void LoadFrame()
        {
            for (int i = 0; i < Human.all.Count; i++)
            {
                if (saveState.humans.TryGetValue(i, out HumanSaveState state))
                {
                    LoadHuman(Human.all[i], state);
                }
            }
            foreach (NetBody netBody in FindObjectsOfType<NetBody>())
            {
                if (netBody.gameObject.scene.name != "DontDestroyOnLoad" && saveState.bodies.TryGetValue(netBody.gameObject.GetComponent<NetIdentity>().sceneId, out NetBodyState netBodyState))
                {
                    Transform transform = netBody.transform;
                    Rigidbody component = netBody.gameObject.GetComponent<Rigidbody>();
                    transform.position = netBodyState.position;
                    transform.rotation = netBodyState.rotation;
                    component.velocity = netBodyState.velocity;
                    component.angularVelocity = netBodyState.angularVelocity;
                }
            }
            Game.instance.passedLevel = saveState.passed;
        }

        public IEnumerator FrameAdvance()
        {
            Time.timeScale = 1;
            yield return new WaitForFixedUpdate();
            Time.timeScale = 0;
            yield break;
        }

        public void LoadGrab(CollisionSensor sensor, GrabState state)
        {
            sensor.ReleaseGrab(0);
            if (state.grabState == 2)
            {
                if (state.grabObject != null)
                {
                    sensor.grabJoint = sensor.gameObject.AddComponent<ConfigurableJoint>();
                    sensor.grabJoint.autoConfigureConnectedAnchor = false;
                    sensor.grabJoint.xMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.yMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.zMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.angularXMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.angularYMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.angularZMotion = ConfigurableJointMotion.Locked;
                    sensor.grabJoint.breakForce = 20000;
                    sensor.grabJoint.breakTorque = 20000;
                    sensor.grabJoint.enablePreprocessing = false;
                    sensor.grabJoint.anchor = state.anchor;
                    sensor.grabJoint.connectedBody = state.connectedBody;
                    sensor.grabJoint.connectedAnchor = state.connectedAnchor;
                    sensor.grabBody = state.grabBody;
                }
            }
            if (state.grabState > 0)
            {
                if (state.grabObject != null)
                {
                    sensor.grabObject = state.grabObject;
                }
            }
            else
            {
                sensor.grabObject = null;
            }
        }

        public void SaveHuman(Human human, HumanSaveState state)
        {
            state.bodies.Initialize();
            for (int i = 0; i < human.rigidbodies.Length; i++)
            {
                Rigidbody rigidbody = human.rigidbodies[i];
                Body body = new Body
                {
                    transform = rigidbody.transform,
                    rigidbody = rigidbody,
                    position = rigidbody.transform.position,
                    rotation = rigidbody.transform.rotation,
                    velocity = rigidbody.velocity,
                    angularVelocity = rigidbody.angularVelocity
                };
                state.bodies[i] = body;
            }
            state.humanState = human.state;
            state.cameraPitchAngle = human.controls.cameraPitchAngle;
            state.cameraYawAngle = human.controls.cameraYawAngle;
            state.grabbedObjects.Clear();
            state.grabbedObjects.AddRange(((GrabManager)PFUtil.grabManager.GetValue(human)).grabbedObjects);
            state.unconsciousTime = human.unconsciousTime;
            state.grabStartPosition = (Vector3)PFUtil.grabStartPosition.GetValue(human);
            state.fallTimer = (float)PFUtil.fallTimer.GetValue(human);
            state.groundDelay = (float)PFUtil.groundDelay.GetValue(human);
            state.jumpDelay = (float)PFUtil.jumpDelay.GetValue(human);
            state.slideTimer = (float)PFUtil.slideTimer.GetValue(human);
            state.walkSpeed = human.controls.walkSpeed;
            state.leftBlockTime = (float)PFUtil.blockGrab.GetValue(human.ragdoll.partLeftHand.sensor);
            state.rightBlockTime = (float)PFUtil.blockGrab.GetValue(human.ragdoll.partRightHand.sensor);
            state.diveTime = (float)PFUtil.diveTime.GetValue(human.GetComponentInChildren<HumanHead>());
            CollisionSensor sensor = human.ragdoll.partLeftHand.sensor;
            GrabState grabState = new GrabState
            {
                grabState = (sensor.grabObject != null) ? ((sensor.grabJoint != null) ? 2 : 1) : 0,
                grabObject = sensor.grabObject,
                grabBody = sensor.grabBody,
                anchor = (sensor.grabJoint != null) ? sensor.grabJoint.anchor : Vector3.zero,
                connectedBody = sensor.grabJoint?.connectedBody,
                connectedAnchor = (sensor.grabJoint != null) ? sensor.grabJoint.connectedAnchor : Vector3.zero
            };
            state.leftGrabState = grabState;
            sensor = human.ragdoll.partRightHand.sensor;
            grabState = new GrabState
            {
                grabState = (sensor.grabObject != null) ? ((sensor.grabJoint != null) ? 2 : 1) : 0,
                grabObject = sensor.grabObject,
                grabBody = sensor.grabBody,
                anchor = (sensor.grabJoint != null) ? sensor.grabJoint.anchor : Vector3.zero,
                connectedBody = sensor.grabJoint?.connectedBody,
                connectedAnchor = (sensor.grabJoint != null) ? sensor.grabJoint.connectedAnchor : Vector3.zero
            };
            state.rightGrabState = grabState;
        }

        public void LoadHuman(Human human, HumanSaveState state)
        {
            for (int i = 0; i < human.rigidbodies.Length; i++)
            {
                Body body = state.bodies[i];
                Rigidbody rigidbody = human.rigidbodies[i];
                rigidbody.transform.position = body.position;
                rigidbody.transform.rotation = body.rotation;
                rigidbody.velocity = body.velocity;
                rigidbody.angularVelocity = body.angularVelocity;
            }
            human.state = state.humanState;
            human.controls.cameraPitchAngle = state.cameraPitchAngle;
            human.controls.cameraYawAngle = state.cameraYawAngle;
            ((GrabManager)PFUtil.grabManager.GetValue(human)).grabbedObjects.Clear();
            ((GrabManager)PFUtil.grabManager.GetValue(human)).grabbedObjects.AddRange(state.grabbedObjects);
            human.unconsciousTime = state.unconsciousTime;
            PFUtil.grabStartPosition.SetValue(human, state.grabStartPosition);
            PFUtil.fallTimer.SetValue(human, state.fallTimer);
            PFUtil.groundDelay.SetValue(human, state.groundDelay);
            PFUtil.jumpDelay.SetValue(human, state.jumpDelay);
            PFUtil.slideTimer.SetValue(human, state.slideTimer);
            human.controls.walkSpeed = state.walkSpeed;
            PFUtil.blockGrab.SetValue(human.ragdoll.partLeftHand.sensor, state.leftBlockTime);
            PFUtil.blockGrab.SetValue(human.ragdoll.partRightHand.sensor, state.rightBlockTime);
            if (saveHand)
            {
                LoadGrab(human.ragdoll.partLeftHand.sensor, state.leftGrabState);
                LoadGrab(human.ragdoll.partRightHand.sensor, state.rightGrabState);
            }
            PFUtil.diveTime.SetValue(human.GetComponentInChildren<HumanHead>(), state.diveTime);
        }

        public void SaveGameState()
        {
            saveFrames.Clear();
            saveFrames.AddRange(currentFrames);
            saveFrames.RemoveRange(currentFrame, currentFrames.Count - currentFrame);
        }

        public void LoadGameState()
        {
            currentFrames.Clear();
            currentFrames.AddRange(saveFrames);
            currentFrame = saveFrames.Count - 1;
            saveState = currentFrames[currentFrame];
            LoadFrame();
        }

        public string WriteInt(int num, string s)
        {
            return s + num.ToString() + ",";
        }
        
        public string WriteFloat(float num, string s)
        {
            return s + num.ToString() + ",";
        }
        
        public string ReadInt(string s, out int num)
        {
            int num2 = s.IndexOf(',');
            num = Convert.ToInt16(s.Substring(0, num2));
            return s.Substring(num2 + 1);
        }
        
        public string ReadFloat(string s, out float num)
        {
            int num2 = s.IndexOf(',');
            num = (float)Convert.ToDouble(s.Substring(0, num2));
            return s.Substring(num2 + 1);
        }

        public string WriteFrame(string s)
        {
            s += "f";
            s = WriteInt(saveState.frame, s);
            s = WriteInt(saveState.passed ? 1 : 0, s);
            s += "\n";
            foreach (KeyValuePair<int, HumanSaveState> keyValuePair in saveState.humans)
            {
                s += "h";
                s = WriteInt(keyValuePair.Key, s);
                HumanSaveState value = keyValuePair.Value;
                s = WriteInt(value.bodies.Length, s);
                for (int i = 0; i < value.bodies.Length; i++)
                {
                    s = WriteFloat(value.bodies[i].position.x, s);
                    s = WriteFloat(value.bodies[i].position.y, s);
                    s = WriteFloat(value.bodies[i].position.z, s);
                    s = WriteFloat(value.bodies[i].rotation.x, s);
                    s = WriteFloat(value.bodies[i].rotation.y, s);
                    s = WriteFloat(value.bodies[i].rotation.z, s);
                    s = WriteFloat(value.bodies[i].rotation.w, s);
                    s = WriteFloat(value.bodies[i].velocity.x, s);
                    s = WriteFloat(value.bodies[i].velocity.y, s);
                    s = WriteFloat(value.bodies[i].velocity.z, s);
                    s = WriteFloat(value.bodies[i].angularVelocity.x, s);
                    s = WriteFloat(value.bodies[i].angularVelocity.y, s);
                    s = WriteFloat(value.bodies[i].angularVelocity.z, s);
                }
                s = WriteInt((int)value.humanState, s);
                s = WriteFloat(value.cameraPitchAngle, s);
                s = WriteFloat(value.cameraYawAngle, s);
                s = WriteFloat(value.unconsciousTime, s);
                s = WriteFloat(value.grabStartPosition.x, s);
                s = WriteFloat(value.grabStartPosition.y, s);
                s = WriteFloat(value.grabStartPosition.z, s);
                s = WriteFloat(value.fallTimer, s);
                s = WriteFloat(value.groundDelay, s);
                s = WriteFloat(value.jumpDelay, s);
                s = WriteFloat(value.slideTimer, s);
                s = WriteFloat(value.walkSpeed, s);
                s = WriteFloat(value.leftBlockTime, s);
                s = WriteFloat(value.rightBlockTime, s);
                s = WriteInt(value.leftGrabState.grabState, s);
                s = WriteInt(value.rightGrabState.grabState, s);
                s = WriteFloat(value.diveTime, s);
                s += "\n";
            }
            foreach (KeyValuePair<uint, NetBodyState> keyValuePair2 in saveState.bodies)
            {
                s += "b";
                s = WriteInt((int)keyValuePair2.Key, s);
                s = WriteFloat(keyValuePair2.Value.position.x, s);
                s = WriteFloat(keyValuePair2.Value.position.y, s);
                s = WriteFloat(keyValuePair2.Value.position.z, s);
                s = WriteFloat(keyValuePair2.Value.rotation.x, s);
                s = WriteFloat(keyValuePair2.Value.rotation.y, s);
                s = WriteFloat(keyValuePair2.Value.rotation.z, s);
                s = WriteFloat(keyValuePair2.Value.rotation.w, s);
                s = WriteFloat(keyValuePair2.Value.velocity.x, s);
                s = WriteFloat(keyValuePair2.Value.velocity.y, s);
                s = WriteFloat(keyValuePair2.Value.velocity.z, s);
                s = WriteFloat(keyValuePair2.Value.angularVelocity.x, s);
                s = WriteFloat(keyValuePair2.Value.angularVelocity.y, s);
                s = WriteFloat(keyValuePair2.Value.angularVelocity.z, s);
                s += "\n";
            }
            s += "d\n";
            return s;
        }

        public IEnumerator SaveFile(string name)
        {
            if (File.Exists(path + name + ".txt"))
            {
                fileProgress = 5;
                yield break;
            }
            List<string> strings = new List<string>();
            int num;
            for (int i = 0; i < currentFrames.Count; i = num + 1)
            {
                saveState = currentFrames[i];
                string item = WriteFrame("");
                strings.Add(item);
                fileProgress = i / (float)currentFrames.Count;
                yield return null;
                num = i;
            }
            fileProgress = 2;
            StreamWriter streamWriter = File.CreateText(path + name + ".txt");
            streamWriter.Write(string.Format("v{0},\n", version.ToString()));
            for (int j = 0; j < strings.Count; j++)
            {
                streamWriter.Write(strings[j]);
            }
            streamWriter.Close();
            fileProgress = 3;
            yield break;
        }
        
        public IEnumerator LoadFile(string name)
        {
            string text = File.ReadAllText(path + name + ".txt");

            if (text[0] == 'v')
            {
                text = text.Substring(1);
                text = ReadInt(text, out readVersion);
            }
            else
            {
                readVersion = 0;
            }
            if (readVersion > version)
            {
                fileProgress = 4;
                yield break;
            }
            string[] strings = text.Split(new char[]
            {
            'd'
            }, StringSplitOptions.RemoveEmptyEntries);
            int length = strings.Length;
            currentFrames.Clear();
            int num;
            for (int i = 0; i < strings.Length; i = num + 1)
            {
                ReadFrame(strings[i] + "d");
                currentFrames.Add(saveState);
                fileProgress = i / (float)length;
                yield return null;
                num = i;
            }
            fileProgress = 3;
            yield break;
        }
        
        public string ReadFrame(string s)
        {
            saveState = new SaveState
            {
                humans = new Dictionary<int, HumanSaveState>(),
                bodies = new Dictionary<uint, NetBodyState>()
            };
            foreach (string t in s.Split(new char[]{'\n'}, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = t;
                switch (text[0])
                {
                    case 'b':
                        {
                            text = text.Substring(1);
                            text = ReadInt(text, out int key);
                            text = ReadFloat(text, out float x);
                            text = ReadFloat(text, out float y);
                            text = ReadFloat(text, out float z);
                            text = ReadFloat(text, out float x2);
                            text = ReadFloat(text, out float y2);
                            text = ReadFloat(text, out float z2);
                            text = ReadFloat(text, out float w);
                            text = ReadFloat(text, out float x3);
                            text = ReadFloat(text, out float y3);
                            text = ReadFloat(text, out float z3);
                            float x4 = 0;
                            float y4 = 0;
                            float z4 = 0;
                            if (readVersion > 1)
                            {
                                text = ReadFloat(text, out x4);
                                text = ReadFloat(text, out y4);
                                text = ReadFloat(text, out z4);
                            }
                            saveState.bodies[(uint)key] = new NetBodyState
                            {
                                position = new Vector3(x, y, z),
                                rotation = new Quaternion(x2, y2, z2, w),
                                velocity = new Vector3(x3, y3, z3),
                                angularVelocity = new Vector3(x4, y4, z4)
                            };
                            break;
                        }
                    case 'd':
                        return "";
                    case 'f':
                        text = text.Substring(1);
                        text = ReadInt(text, out saveState.frame);
                        if (readVersion > 3)
                        {
                            text = ReadInt(text, out int num);
                            saveState.passed = num == 1;
                        }
                        break;
                    case 'h':
                        {
                            text = text.Substring(1);
                            HumanSaveState humanSaveState = new HumanSaveState();
                            text = ReadInt(text, out int key2);
                            text = ReadInt(text, out int num);
                            for (int j = 0; j < num; j++)
                            {
                                text = ReadFloat(text, out float x5);
                                text = ReadFloat(text, out float y5);
                                text = ReadFloat(text, out float z5);
                                text = ReadFloat(text, out float x6);
                                text = ReadFloat(text, out float y6);
                                text = ReadFloat(text, out float z6);
                                text = ReadFloat(text, out float w2);
                                text = ReadFloat(text, out float x7);
                                text = ReadFloat(text, out float y7);
                                text = ReadFloat(text, out float z7);
                                float x8 = 0;
                                float y8 = 0;
                                float z8 = 0;
                                if (readVersion > 1)
                                {
                                    text = ReadFloat(text, out x8);
                                    text = ReadFloat(text, out y8);
                                    text = ReadFloat(text, out z8);
                                }
                                humanSaveState.bodies[j] = new Body
                                {
                                    position = new Vector3(x5, y5, z5),
                                    rotation = new Quaternion(x6, y6, z6, w2),
                                    velocity = new Vector3(x7, y7, z7),
                                    angularVelocity = new Vector3(x8, y8, z8)
                                };
                            }
                            text = ReadInt(text, out int humanState);
                            humanSaveState.humanState = (HumanState)humanState;
                            text = ReadFloat(text, out humanSaveState.cameraPitchAngle);
                            text = ReadFloat(text, out humanSaveState.cameraYawAngle);
                            text = ReadFloat(text, out humanSaveState.unconsciousTime);
                            text = ReadFloat(text, out humanSaveState.grabStartPosition.x);
                            text = ReadFloat(text, out humanSaveState.grabStartPosition.y);
                            text = ReadFloat(text, out humanSaveState.grabStartPosition.z);
                            text = ReadFloat(text, out humanSaveState.fallTimer);
                            text = ReadFloat(text, out humanSaveState.groundDelay);
                            text = ReadFloat(text, out humanSaveState.jumpDelay);
                            text = ReadFloat(text, out humanSaveState.slideTimer);
                            text = ReadFloat(text, out humanSaveState.walkSpeed);
                            if (readVersion > 4)
                            {
                                text = ReadFloat(text, out humanSaveState.leftBlockTime);
                                text = ReadFloat(text, out humanSaveState.rightBlockTime);
                            }
                            if (readVersion > 2)
                            {
                                text = ReadInt(text, out humanSaveState.leftGrabState.grabState);
                                text = ReadInt(text, out humanSaveState.rightGrabState.grabState);
                            }
                            if (readVersion > 5)
                            {
                                text = ReadFloat(text, out humanSaveState.diveTime);
                            }
                            saveState.humans[key2] = humanSaveState;
                            break;
                        }
                }
            }
            return "";
        }

        [HarmonyPatch(typeof(Game), "Resume")]
        [HarmonyPostfix]
        public static void Resume()
        {
            Time.timeScale = speed;
        }

        [HarmonyPatch(typeof(Game), "Fall")]
        [HarmonyPrefix]
        public static bool Fall()
        {
            if (ReplayRecorder.isPlaying || NetGame.isClient)
            {
                return true;
            }
            if (!Game.instance.passedLevel || ((Game.instance.currentLevelNumber < 0 || Game.instance.currentLevelNumber >= Game.instance.levels.Length) && Game.instance.workshopLevel == null && Game.instance.currentLevelType != WorkshopItemSource.EditorPick))
            {
                return true;
            }
            if (tasMode)
            {
                readonlyMode = true;
                Physics.autoSimulation = false;
                Time.timeScale = 1;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(HumanControls), "HandleInput")]
        [HarmonyPostfix]
        public static void HandleInput(HumanControls __instance)
        {
            if (tasMode && !readonlyMode && autoGetUp && ((Human)PFUtil.humanScript.GetValue(__instance)).state == HumanState.Spawning)
            {
                __instance.unconscious = true;
            }
        }

        [HarmonyPatch(typeof(Game), "RespawnAllPlayers", new Type[] { })]
        [HarmonyPrefix]
        public static bool RespawnAllPlayers()
        {
            if (tasMode && !readonlyMode && modifySpawn && int.TryParse(curSpawn, out int spawn))
            {
                Checkpoint checkpoint = Game.currentLevel.GetCheckpoint(Game.instance.currentCheckpointNumber);
                for (int i = 0; i < Human.all.Count; i++)
                {
                    float d = (!checkpoint.tightSpawn) ? 2f : 0.5f;
                    Game.instance.Respawn(Human.all[i], Vector3.left * ((spawn - 1) % 3) * d + Vector3.back * ((spawn - 1) / 3) * d);
                }
                return false;
            }
            return true;
        }

        public bool guiOpened;
        public bool debug;
        public Rect windowRect;
        public Rect freezeRect;
        public Color32 color;
        
        public static bool tasMode;
        public static bool readonlyMode;
        public static bool autoGetUp;
        public static bool freeze;
        public static bool saveHand;
        public static bool modifySpawn;
        public static string curSpawn;

        public readonly string modVersion = "v1.6.7";
        public int readVersion;
        public readonly int version = 6;
        public string path = "Movies/";

        public GameState oldState;
        public static int currentFrame;
        public SaveState saveState;
        public List<SaveState> currentFrames;
        public List<SaveState> saveFrames;

        public float fileProgress;
        public static float speed = 1;
        public static bool playing;

        public string curSpeed;
        public string fileName;

        public List<uint> frozenObjects = new List<uint>();
        public uint selectedObject;
        public string curFreeze;
    }
}
