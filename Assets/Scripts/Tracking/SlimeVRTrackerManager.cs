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

    private void Awake()
    {
        Instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        slimeBridge = SlimeVRBridge.getDriverInstance();
        slimeBridge.connect();

        vmcBridge = SlimeVRBridge.getVMCInstance();
        vmcBridge.connect();
    }

    Messages.ProtobufMessage EmptyMessage = new Messages.ProtobufMessage();
    // Update is called once per frame
    void Update()
    {
        bool slime_bridge_is_connected = slimeBridge.sendMessage(EmptyMessage) && slimeBridge.flush();
        if (!slime_bridge_is_connected)
        {
            //Debug.Log("slime bridge is not connected");
        }
        else
        {
            while (true)
            {
                foreach (var t in slimeTrackers)
                    if (t != null) t.isOK = false;
                Messages.ProtobufMessage message = slimeBridge.getNextMessage();
                if (message == null)
                    break;
                if (message.Position != null)
                    HandleTrackerPositionSlimeVR(message.Position);
                else if (message.TrackerAdded != null)
                    HandleTrackerAddSlimeVR(message.TrackerAdded);

                foreach (var t in slimeTrackers)
                    if (t != null) TrackingPointManager.Instance.ApplyPoint(t.name, t.deviceType, t.Position, t.Rotation, t.isOK);
            }
        }
        bool vmc_bridge_is_connected = vmcBridge.sendMessage(EmptyMessage) && vmcBridge.flush();
        if (!vmc_bridge_is_connected)
        {
            //Debug.Log("vmc bridge is not connected");
        }
        else
        {
            foreach (var t in vmcTrackers)
                if (t != null) t.isOK = false;
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
            foreach (var t in vmcTrackers)
                if (t != null) TrackingPointManager.Instance.ApplyPoint(t.name, t.deviceType, t.Position, t.Rotation, t.isOK);
        }
    }

    class SlimeVRTrackerInfo
    {
        public bool isOK = false;
        public Vector3 Position;
        public Quaternion Rotation;
        public string name;
        public Valve.VR.ETrackedDeviceClass deviceType;
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
    }

    void HandleTrackerAddSlimeVR(Messages.TrackerAdded trackerAdded)
    {
        slimeTrackers[trackerAdded.TrackerId] = new SlimeVRTrackerInfo()
        {
            name = trackerAdded.TrackerSerial,
            deviceType =
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.Head ? Valve.VR.ETrackedDeviceClass.HMD :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.LeftController ? Valve.VR.ETrackedDeviceClass.Controller :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.RightController ? Valve.VR.ETrackedDeviceClass.Controller :
                Valve.VR.ETrackedDeviceClass.GenericTracker
        };
    }
    void HandleTrackerAddVMC(Messages.TrackerAdded trackerAdded)
    {
        vmcTrackers[trackerAdded.TrackerId] = new SlimeVRTrackerInfo()
        {
            name = trackerAdded.TrackerSerial,
            deviceType =
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.Head ? Valve.VR.ETrackedDeviceClass.HMD :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.LeftController ? Valve.VR.ETrackedDeviceClass.Controller :
            trackerAdded.TrackerRole == (int)SlimeVRBridge.SlimeVRPosition.RightController ? Valve.VR.ETrackedDeviceClass.Controller :
                Valve.VR.ETrackedDeviceClass.GenericTracker
        };
    }
    private void OnDestroy()
    {
        slimeBridge.close();
        vmcBridge.close();
    }
}
