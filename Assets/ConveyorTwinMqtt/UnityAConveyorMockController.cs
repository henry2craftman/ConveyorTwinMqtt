using UnityEngine;

namespace MPS
{
    /// <summary>
    /// 설비가 없는 교육장에서 쓰는 Unity A용 Mock PLC 컨트롤러.
    ///
    /// 흐름
    ///   1. Start 버튼 -> SpawnObject()
    ///   2. 물체가 근접센서 Trigger에 들어옴 -> X0(objectDetected) ON
    ///   3. Mock PLC 로직이 X0를 보고 Y20(conveyorMotorOn) ON
    ///   4. 컨베이어 애니메이션, RPM/전류/온도/진동 Mock 값 갱신
    ///   5. ConveyorMqttPublisher가 Snapshot을 MQTT로 발행
    /// </summary>
    public class UnityAConveyorMockController : MonoBehaviour
    {
        [Header("PLC Mock 디바이스")]
        public bool y20ConveyorMotorOn;
        public bool x0ObjectDetected;
        public bool startButton;

        [Header("Unity 장비")]
        public Conveyor conveyor;
        public Sensor proximitySensor;
        public Transform objectSpawnPoint;
        public GameObject fallingObjectPrefab;
        public Transform fallbackSpawnParent;
        public ConveyorDashboard dashboard;
        public ConveyorMqttPublisher mqttPublisher;

        [Header("Mock 계측값")]
        public float temperatureC = 28f;
        public float vibrationMmS = 0.2f;
        public float currentA = 0.1f;
        public float rpm;

        [Header("운전 목표값")]
        public float runningTemperatureC = 42f;
        public float runningVibrationMmS = 1.2f;
        public float runningCurrentA = 3.1f;
        public float runningRpm = 1420f;

        [Header("주의/알람 임계값")]
        public float warningTemperatureC = 65f;
        public float alarmTemperatureC = 80f;
        public float warningVibrationMmS = 3.5f;
        public float alarmVibrationMmS = 6f;
        public float warningCurrentA = 4.5f;
        public float alarmCurrentA = 7f;

        [Header("동작 설정")]
        public bool latchMotorAfterDetection = true;
        public bool destroyFallingObjectAfterSeconds = true;
        public float fallingObjectLifetime = 12f;
        public float measurementFollowSpeed = 2.5f;
        public Vector3 motorRotationAxis = Vector3.forward;
        public float motorRotationScale = 0.06f;
        public bool rotateMotorOnlyWhenMotorOn = true;

        public ConveyorTwinMessage CurrentMessage { get; private set; } = new ConveyorTwinMessage();

        GameObject _lastSpawnedObject;

        private void Update()
        {
            if (proximitySensor != null)
                x0ObjectDetected = proximitySensor.sensorSignal;

            RunMockPlcLogic();
            ApplyToUnityObjects();
            UpdateMeasurements();
            BuildCurrentMessage();

            if (dashboard != null)
                dashboard.Apply(CurrentMessage, mqttPublisher == null || mqttPublisher.IsConnected);
        }

        public void PressStartButton()
        {
            startButton = true;
            SpawnObject();
        }

        public void ReleaseStartButton()
        {
            startButton = false;
        }

        public void ResetScenario()
        {
            startButton = false;
            x0ObjectDetected = false;
            y20ConveyorMotorOn = false;

            if (proximitySensor != null)
                proximitySensor.SetState(false);

            if (_lastSpawnedObject != null)
                Destroy(_lastSpawnedObject);
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

            // if (_lastSpawnedObject.GetComponent<Rigidbody>() == null)
            //     _lastSpawnedObject.AddComponent<Rigidbody>();

            // if (_lastSpawnedObject.GetComponent<Collider>() == null)
            //     _lastSpawnedObject.AddComponent<BoxCollider>();

            if (destroyFallingObjectAfterSeconds)
                Destroy(_lastSpawnedObject, fallingObjectLifetime);
        }

        void RunMockPlcLogic()
        {
            if (x0ObjectDetected)
            {
                y20ConveyorMotorOn = true;
                if (latchMotorAfterDetection)
                    startButton = false;
            }

            if (!latchMotorAfterDetection && !x0ObjectDetected)
                y20ConveyorMotorOn = false;
        }

        void ApplyToUnityObjects()
        {
            if (conveyor == null)
                return;

            conveyor.cWSignal = y20ConveyorMotorOn;
            conveyor.cCWSignal = false;
        }

        void UpdateMeasurements()
        {
            float targetRpm = y20ConveyorMotorOn ? runningRpm : 0f;
            float targetCurrent = y20ConveyorMotorOn ? runningCurrentA : 0.1f;
            float targetTemperature = y20ConveyorMotorOn ? runningTemperatureC : 28f;
            float targetVibration = y20ConveyorMotorOn ? runningVibrationMmS : 0.2f;

            float t = Mathf.Clamp01(Time.deltaTime * measurementFollowSpeed);
            rpm = Mathf.Lerp(rpm, targetRpm, t);
            currentA = Mathf.Lerp(currentA, targetCurrent, t);
            temperatureC = Mathf.Lerp(temperatureC, targetTemperature, t * 0.35f);
            vibrationMmS = Mathf.Lerp(vibrationMmS, targetVibration, t);
        }


        void BuildCurrentMessage()
        {
            CurrentMessage.schema = "factory.conveyor.state.v1";
            CurrentMessage.lineId = "line1";
            CurrentMessage.source = "unity-a-mock";
            CurrentMessage.mode = "mock";
            CurrentMessage.timestamp = NowShortTimestamp();
            CurrentMessage.connected = true;

            CurrentMessage.state.startButton = startButton;
            CurrentMessage.state.objectDetected = x0ObjectDetected;
            CurrentMessage.state.conveyorMotorOn = y20ConveyorMotorOn;

            CurrentMessage.measurements.temperatureC = Round1(temperatureC);
            CurrentMessage.measurements.vibrationMmS = Round1(vibrationMmS);
            CurrentMessage.measurements.currentA = Round1(currentA);
            CurrentMessage.measurements.rpm = Mathf.Round(rpm);

            CurrentMessage.raw.Y20 = y20ConveyorMotorOn;
            CurrentMessage.raw.X0 = x0ObjectDetected;
            CurrentMessage.raw.D100 = Mathf.RoundToInt(CurrentMessage.measurements.temperatureC * 10f);
            CurrentMessage.raw.D110 = Mathf.RoundToInt(CurrentMessage.measurements.vibrationMmS * 10f);
            CurrentMessage.raw.D120 = Mathf.RoundToInt(CurrentMessage.measurements.currentA * 10f);
            CurrentMessage.raw.D130 = Mathf.RoundToInt(CurrentMessage.measurements.rpm);

            ApplyStatus(CurrentMessage);
        }

        void ApplyStatus(ConveyorTwinMessage message)
        {
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

        static string NowShortTimestamp()
        {
            return System.DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
}
