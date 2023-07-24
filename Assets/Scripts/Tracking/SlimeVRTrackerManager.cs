using Messages;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VMC;

public class SlimeVRTrackerManager : MonoBehaviour
{
    public static SlimeVRTrackerManager Instance;

    SlimeVRBridge slimeBridge,vmcBridge;


    private bool closed = false;
    private System.Threading.Thread serverThread;
    private void Awake()
    {
        Instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        slimeBridge = SlimeVRBridge.getDriverInstance();
        slime_vr_next_connect = DateTime.Now;

        vmcBridge = SlimeVRBridge.getVMCInstance();
        vmc_next_connect = DateTime.Now;

        serverThread = new System.Threading.Thread(() =>
        {
            while (!closed)
            {
                UpdateFrame();
                System.Threading.Thread.Sleep(15);
            }
            slimeBridge.close();
            vmcBridge.close();
        });
        serverThread.Start();
    }

    Messages.ProtobufMessage EmptyMessage = new Messages.ProtobufMessage();
    // Update is called once per frame
    DateTime slime_vr_next_connect;
    DateTime vmc_next_connect;

    void UpdateFrame()
    {
        DateTime now = DateTime.Now;

         if (slime_vr_next_connect < now)
        {
            slimeBridge.connect();
            slime_vr_next_connect = DateTime.MaxValue;
        }
        bool slime_bridge_is_connected = slimeBridge.sendMessage(EmptyMessage) && slimeBridge.flush();
        if (!slime_bridge_is_connected)
        {
            slimeBridge.reset();
            //Debug.Log("slime bridge is not connected");
            if (slime_vr_next_connect == DateTime.MaxValue)
                slime_vr_next_connect = DateTime.Now.AddSeconds(3);
        }
        else
        {
            while (true)
            {
                foreach (var t in slimeTrackers)
                    if (t != null) t.isOK = (now - t.lastTime).TotalSeconds < 15;
                Messages.ProtobufMessage message = slimeBridge.getNextMessage();
                if (message == null)
                    break;
                if (message.Position != null)
                    HandleTrackerPositionSlimeVR(message.Position);
                else if (message.TrackerAdded != null)
                    HandleTrackerAddSlimeVR(message.TrackerAdded);
            }
        }

        if (vmc_next_connect < now)
        {
            vmcBridge.connect();
            vmc_next_connect = DateTime.MaxValue;
        }

        bool vmc_bridge_is_connected = vmcBridge.sendMessage(EmptyMessage) && vmcBridge.flush();
        if (!vmc_bridge_is_connected)
        {
            //Debug.Log("vmc bridge is not connected");
            if (vmc_next_connect == DateTime.MaxValue)
                vmc_next_connect = DateTime.Now.AddSeconds(3);
        }
        else
        {
            foreach (var t in vmcTrackers)
                if (t != null) t.isOK = (now - t.lastTime).TotalSeconds < 3;
            while (true)
            {
                Messages.ProtobufMessage message = vmcBridge.getNextMessage();
                if (message == null)
                    break;
                if (message.Position != null)
                    HandleTrackerPositionVMC(message.Position);
                else if (message.TrackerAdded != null)
                    HandleTrackerAddVMC(message.TrackerAdded);
            }
        }
    }

    private void Update()
    {
        foreach (var t in slimeTrackers)
            if (t != null && t.isOK) TrackingPointManager.Instance.ApplyPoint(t.name, t.deviceType, t.Position, t.Rotation, true);

        foreach (var t in vmcTrackers)
            if (t != null && t.isOK) TrackingPointManager.Instance.ApplyPoint(t.name, t.deviceType, t.Position, t.Rotation, true);
    }

    class SlimeVRTrackerInfo
    {
        public bool isOK = false;
        public Vector3 Position;
        public Quaternion Rotation;
        public string name;
        public Valve.VR.ETrackedDeviceClass deviceType;

        public DateTime lastTime;
    }

    SlimeVRTrackerInfo[] slimeTrackers = new SlimeVRTrackerInfo[20];
    SlimeVRTrackerInfo[] vmcTrackers = new SlimeVRTrackerInfo[5];
    private void HandleTrackerPositionSlimeVR(Position position)
    {
        if (position.TrackerId > slimeTrackers.Length)
            return;
        SlimeVRTrackerInfo trackerInfo = slimeTrackers[position.TrackerId];
        if (trackerInfo == null)
            return;
        trackerInfo.Position = new Vector3(position.X, position.Y, -position.Z);
        trackerInfo.Rotation = new Quaternion(-position.Qx, position.Qy, position.Qz, -position.Qw);
        trackerInfo.isOK = true;
        trackerInfo.lastTime = DateTime.Now;
    }

    private void HandleTrackerPositionVMC(Position position)
    {
        if (position.TrackerId > vmcTrackers.Length)
            return;
        SlimeVRTrackerInfo trackerInfo = vmcTrackers[position.TrackerId];
        if (trackerInfo == null)
            return;
        trackerInfo.Position = new Vector3(position.X, position.Y, position.Z);
        trackerInfo.Rotation = new Quaternion(position.Qx, position.Qy, position.Qz, position.Qw);
        trackerInfo.isOK = true;
        trackerInfo.lastTime = DateTime.Now;
    }

    void HandleTrackerAddSlimeVR(Messages.TrackerAdded trackerAdded)
    {
        slimeTrackers[trackerAdded.TrackerId] = new SlimeVRTrackerInfo()
        {
            name = trackerAdded.TrackerSerial,
            deviceType = Valve.VR.ETrackedDeviceClass.GenericTracker
        };
    }
    void HandleTrackerAddVMC(Messages.TrackerAdded trackerAdded)
    {
        vmcTrackers[trackerAdded.TrackerId] = new SlimeVRTrackerInfo()
        {
            name = trackerAdded.TrackerSerial,
            deviceType =
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.HMD ? Valve.VR.ETrackedDeviceClass.HMD :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.LeftController ? Valve.VR.ETrackedDeviceClass.Controller :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.RightController ? Valve.VR.ETrackedDeviceClass.Controller :
                Valve.VR.ETrackedDeviceClass.GenericTracker
        };
    }
    private void OnDestroy()
    {
        closed = true;
        serverThread.Join();
    }
}
