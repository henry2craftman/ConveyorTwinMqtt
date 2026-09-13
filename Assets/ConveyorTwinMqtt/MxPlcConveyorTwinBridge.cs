using UnityEngine;

namespace MPS
{
    /// <summary>
    /// ConveyorTwinMqtt 전용 단독 PLC 컨트롤러입니다.
    /// MxPlcController 컴포넌트 없이 MxPlcNative를 직접 사용해서
    /// PLC 출력 Y20(컨베이어 모터)을 읽어 MQTT 상태로 변환합니다.
    /// 시작 버튼 X10은 GX Works2/PLC 래더 쪽에서 직접 처리합니다.
    /// </summary>
    public class MxPlcConveyorTwinBridge : MonoBehaviour
    {
        const int X0ObjectDetectedIndex = 0;

        [Header("PLC Connection")]
        public int logicalStationNumber = 0;
        public int pollIntervalUs = 1000;
        public int cpuCore = -1;
        public bool connectOnStart;

        [Header("Device Map")]
        public string yStartDevice = "Y20";
        public string xStartDevice = "X0";
        public int yDeviceCount = 1;
        public int xDeviceCount = 1;

        [Header("PLC Device State")]
        public bool y20ConveyorMotorOn;
        public bool x0ObjectDetected;
        public bool startButton;
        public bool isConnected;

        [Header("Unity Equipment")]
        public Conveyor conveyor;
        public Sensor proximitySensor;
        public Transform objectSpawnPoint;
        public GameObject fallingObjectPrefab;
        public Transform fallbackSpawnParent;

        [Header("Optional UI")]
        public ConveyorDashboard dashboard;
        public ConveyorMqttPublisher mqttPublisher;

        [Header("Message Identity")]
        public string lineId = "line1";
        public string source = "unity-a-mx-plc";
        public string mode = "mx-plc";

        [Header("Measured Values")]
        public float temperatureC = 28f;
        public float vibrationMmS = 0.2f;
        public float currentA = 0.1f;
        public float rpm;

        [Header("Running Targets")]
        public float runningTemperatureC = 42f;
        public float runningVibrationMmS = 1.2f;
        public float runningCurrentA = 3.1f;
        public float runningRpm = 1420f;

        [Header("Warning / Alarm Thresholds")]
        public float warningTemperatureC = 65f;
        public float alarmTemperatureC = 80f;
        public float warningVibrationMmS = 3.5f;
        public float alarmVibrationMmS = 6f;
        public float warningCurrentA = 4.5f;
        public float alarmCurrentA = 7f;

        [Header("Motion")]
        public bool spawnObjectOnY20RisingEdge = true;
        public bool destroyFallingObjectAfterSeconds = true;
        public float fallingObjectLifetime = 12f;
        public float measurementFollowSpeed = 2.5f;
        public Vector3 motorRotationAxis = Vector3.forward;
        public float motorRotationScale = 0.06f;
        public bool rotateMotorOnlyWhenMotorOn = true;

        public ConveyorTwinMessage CurrentMessage { get; private set; } = new ConveyorTwinMessage();

        short[] _yRaw;
        short[] _xRaw;
        bool _plcErrorWarned;
        bool _previousY20ConveyorMotorOn;
        GameObject _lastSpawnedObject;

        private void Awake()
        {
            AllocateBuffers();
        }

        private void OnValidate()
        {
            EnsureXDeviceMap();
        }

        private void Reset()
        {
            conveyor = FindFirstObjectByType<Conveyor>();
            proximitySensor = FindFirstObjectByType<Sensor>();
            dashboard = FindFirstObjectByType<ConveyorDashboard>();
            mqttPublisher = FindFirstObjectByType<ConveyorMqttPublisher>();
        }

        private void Start()
        {
            if (connectOnStart)
                Open();
        }

        private void Update()
        {
            PollPlcIfConnected();
            UpdateMeasurements(y20ConveyorMotorOn);
            BuildCurrentMessage();

            if (dashboard != null)
                dashboard.Apply(CurrentMessage, mqttPublisher == null || mqttPublisher.IsConnected);
        }

        public void Open()
        {
            if (isConnected)
                return;

            AllocateBuffers();

            int err = MxPlcNative.MxInit(
                logicalStationNumber,
                yStartDevice,
                yDeviceCount,
                xStartDevice,
                xDeviceCount,
                pollIntervalUs);

            if (err != 0)
            {
                Debug.LogWarning($"[MxPlcConveyorTwinBridge] MxInit failed: {err}");
                return;
            }

            err = MxPlcNative.MxStart(cpuCore);
            if (err != 0)
            {
                Debug.LogWarning($"[MxPlcConveyorTwinBridge] MxStart failed: {err}");
                MxPlcNative.MxDispose();
                return;
            }

            isConnected = true;
            _plcErrorWarned = false;
            Debug.Log($"[MxPlcConveyorTwinBridge] Connected. Y@{yStartDevice} x{yDeviceCount}, X@{xStartDevice} x{xDeviceCount}");
        }

        public void Close()
        {
            if (!isConnected)
                return;

            MxPlcNative.MxDispose();
            isConnected = false;
            y20ConveyorMotorOn = false;
            ApplyY20ToUnity();
            Debug.Log("[MxPlcConveyorTwinBridge] Disconnected.");
        }

        public void PressStartButton()
        {
            startButton = true;
            Debug.Log("[MxPlcConveyorTwinBridge] Start button is local only in PLC mode. Use GX Works2/PLC X10 to drive Y20.");
        }

        public void ReleaseStartButton()
        {
            startButton = false;
        }

        public void SetObjectDetected(bool detected)
        {
            x0ObjectDetected = detected;

            if (proximitySensor != null)
                proximitySensor.SetState(detected);
        }

        public ConveyorTwinMessage GetSnapshot()
        {
            BuildCurrentMessage();
            return CurrentMessage;
        }

        void AllocateBuffers()
        {
            EnsureXDeviceMap();

            _yRaw = new short[Mathf.Max(1, yDeviceCount)];
            _xRaw = new short[Mathf.Max(1, xDeviceCount)];
        }

        void EnsureXDeviceMap()
        {
            xStartDevice = "X0";
            xDeviceCount = 1;
        }

        void PollPlcIfConnected()
        {
            if (!isConnected)
                return;

            int status = MxPlcNative.MxGetStatus();
            if (status == 2)
            {
                if (!_plcErrorWarned)
                {
                    Debug.LogWarning($"[MxPlcConveyorTwinBridge] PLC communication error: 0x{MxPlcNative.MxGetLastPlcError():X}");
                    _plcErrorWarned = true;
                }

                return;
            }

            _plcErrorWarned = false;

            MxPlcNative.MxReadY(_yRaw, yDeviceCount, out _);
            ApplyYData();
        }

        void ApplyYData()
        {
            y20ConveyorMotorOn = (_yRaw[0] & 1) != 0;

            if (spawnObjectOnY20RisingEdge && y20ConveyorMotorOn && !_previousY20ConveyorMotorOn)
                SpawnObject();

            _previousY20ConveyorMotorOn = y20ConveyorMotorOn;
            ApplyY20ToUnity();
        }

        void ApplyY20ToUnity()
        {
            if (conveyor == null)
                return;

            conveyor.cWSignal = y20ConveyorMotorOn;
            conveyor.cCWSignal = false;
        }

        void BuildXData()
        {
            if (proximitySensor != null)
                x0ObjectDetected = proximitySensor.sensorSignal;

            ClearXData();
            _xRaw[X0ObjectDetectedIndex] = (short)(x0ObjectDetected ? 1 : 0);
        }

        void ClearXData()
        {
            for (int i = 0; i < _xRaw.Length; i++)
                _xRaw[i] = 0;
        }

        void SpawnObject()
        {
            Vector3 position = objectSpawnPoint != null
                ? objectSpawnPoint.position
                : transform.position + Vector3.up * 2.5f;

            Quaternion rotation = objectSpawnPoint != null
                ? objectSpawnPoint.rotation
                : Quaternion.identity;

            _lastSpawnedObject = fallingObjectPrefab != null
                ? Instantiate(fallingObjectPrefab, position, rotation)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (fallingObjectPrefab == null)
            {
                _lastSpawnedObject.transform.position = position;
                _lastSpawnedObject.transform.rotation = rotation;
                _lastSpawnedObject.transform.localScale = Vector3.one * 0.35f;
            }

            if (fallbackSpawnParent != null)
                _lastSpawnedObject.transform.SetParent(fallbackSpawnParent);

            if (destroyFallingObjectAfterSeconds)
                Destroy(_lastSpawnedObject, fallingObjectLifetime);
        }

        void UpdateMeasurements(bool motorOn)
        {
            float targetRpm = motorOn ? runningRpm : 0f;
            float targetCurrent = motorOn ? runningCurrentA : 0.1f;
            float targetTemperature = motorOn ? runningTemperatureC : 28f;
            float targetVibration = motorOn ? runningVibrationMmS : 0.2f;

            float t = Mathf.Clamp01(Time.deltaTime * measurementFollowSpeed);
            rpm = Mathf.Lerp(rpm, targetRpm, t);
            currentA = Mathf.Lerp(currentA, targetCurrent, t);
            temperatureC = Mathf.Lerp(temperatureC, targetTemperature, t * 0.35f);
            vibrationMmS = Mathf.Lerp(vibrationMmS, targetVibration, t);
        }


        void BuildCurrentMessage()
        {
            CurrentMessage.schema = "factory.conveyor.state.v1";
            CurrentMessage.lineId = lineId;
            CurrentMessage.source = source;
            CurrentMessage.mode = mode;
            CurrentMessage.timestamp = ConveyorTwinMessage.NowIso();
            CurrentMessage.connected = isConnected;

            CurrentMessage.state.startButton = startButton;
            CurrentMessage.state.objectDetected = x0ObjectDetected;
            CurrentMessage.state.conveyorMotorOn = y20ConveyorMotorOn;

            CurrentMessage.measurements.temperatureC = Round1(temperatureC);
            CurrentMessage.measurements.vibrationMmS = Round1(vibrationMmS);
            CurrentMessage.measurements.currentA = Round1(currentA);
            CurrentMessage.measurements.rpm = Mathf.Round(rpm);

            CurrentMessage.raw.Y20 = y20ConveyorMotorOn;
            CurrentMessage.raw.X0 = x0ObjectDetected;
            CurrentMessage.raw.X10 = startButton;
            CurrentMessage.raw.D100 = Mathf.RoundToInt(CurrentMessage.measurements.temperatureC * 10f);
            CurrentMessage.raw.D110 = Mathf.RoundToInt(CurrentMessage.measurements.vibrationMmS * 10f);
            CurrentMessage.raw.D120 = Mathf.RoundToInt(CurrentMessage.measurements.currentA * 10f);
            CurrentMessage.raw.D130 = Mathf.RoundToInt(CurrentMessage.measurements.rpm);

            ApplyStatus(CurrentMessage);
        }

        void ApplyStatus(ConveyorTwinMessage message)
        {
            if (!isConnected)
            {
                message.quality = "bad";
                message.status.level = "offline";
                message.status.message = "PLC is not connected";
                return;
            }

            bool alarm = temperatureC >= alarmTemperatureC
                || vibrationMmS >= alarmVibrationMmS
                || currentA >= alarmCurrentA;

            bool warning = temperatureC >= warningTemperatureC
                || vibrationMmS >= warningVibrationMmS
                || currentA >= warningCurrentA;

            if (alarm)
            {
                message.quality = "bad";
                message.status.level = "alarm";
                message.status.message = "Conveyor stopped by abnormal current, temperature, or vibration";
            }
            else if (warning)
            {
                message.quality = "good";
                message.status.level = "warning";
                message.status.message = "Temperature, current, or vibration is rising";
            }
            else if (y20ConveyorMotorOn)
            {
                message.quality = "good";
                message.status.level = "normal";
                message.status.message = "Conveyor running normally";
            }
            else
            {
                message.quality = "good";
                message.status.level = "idle";
                message.status.message = "Conveyor is stopped";
            }
        }

        static float Round1(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}
