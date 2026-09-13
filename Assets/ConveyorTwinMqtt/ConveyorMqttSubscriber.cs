using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace MPS
{
    /// <summary>
    /// Unity B에서 MQTT 상태 메시지를 구독하고 컨베이어/대시보드에 반영합니다.
    /// MQTT 수신은 백그라운드 스레드에서 오므로 Queue에 넣고 Update에서 Unity 오브젝트에 적용합니다.
    /// </summary>
    public class ConveyorMqttSubscriber : MonoBehaviour
    {
        [Header("MQTT Broker")]
        public string brokerHost = "127.0.0.1";
        public int brokerPort = 1883;
        public string clientId = "unity-b-conveyor-subscriber";
        public string stateTopic = ConveyorMqttTopics.State;
        public bool connectOnStart = true;

        [Header("Unity B 반영 대상")]
        public Conveyor conveyor;
        public Sensor proximitySensor;
        public Transform motorRotor;
        public float motorRotationScale = 0.06f;
        public Renderer motorStatusRenderer;
        public Renderer objectSensorRenderer;
        public ConveyorDashboard dashboard;

        [Header("디버그")]
        public bool logReceivedJson;

        readonly MqttLiteClient _client = new MqttLiteClient();
        readonly Queue<ConveyorTwinMessage> _pendingMessages = new Queue<ConveyorTwinMessage>();
        readonly object _queueLock = new object();

        ConveyorTwinMessage _latestMessage;
        bool _isConnecting;

        public bool IsConnected => _client.IsConnected;
        public ConveyorTwinMessage LatestMessage => _latestMessage;

        private async void Start()
        {
            _client.MessageReceived += OnMqttMessageReceived;
            _client.Error += ex => Debug.LogWarning($"[MQTT Subscriber] {ex.Message}");

            if (connectOnStart)
                await ConnectAndSubscribeAsync();
        }

        private void Update()
        {
            DrainMessages();
            RotateMotorVisual();
        }

        public async System.Threading.Tasks.Task ConnectAndSubscribeAsync()
        {
            if (_isConnecting || _client.IsConnected)
                return;

            _isConnecting = true;
            try
            {
                await _client.ConnectAsync(brokerHost, brokerPort, clientId);
                await _client.SubscribeAsync(stateTopic);
                Debug.Log($"[MQTT Subscriber] Subscribed: {stateTopic}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQTT Subscriber] Connect failed: {ex.Message}");
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        void OnMqttMessageReceived(string topic, string payload)
        {
            if (topic != stateTopic)
                return;

            try
            {
                ConveyorTwinMessage message = JsonConvert.DeserializeObject<ConveyorTwinMessage>(payload);
                if (message == null)
                    return;

                lock (_queueLock)
                {
                    _pendingMessages.Enqueue(message);
                }

                if (logReceivedJson)
                    Debug.Log($"[MQTT Subscriber] {topic} {payload}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQTT Subscriber] JSON parse failed: {ex.Message}");
            }
        }

        void DrainMessages()
        {
            while (true)
            {
                ConveyorTwinMessage message = null;
                lock (_queueLock)
                {
                    if (_pendingMessages.Count > 0)
                        message = _pendingMessages.Dequeue();
                }

                if (message == null)
                    break;

                ApplyMessage(message);
            }
        }

        void ApplyMessage(ConveyorTwinMessage message)
        {
            _latestMessage = message;

            bool motorOn = message.state != null && message.state.conveyorMotorOn;
            bool objectDetected = message.state != null && message.state.objectDetected;

            if (conveyor != null)
            {
                conveyor.cWSignal = motorOn;
                conveyor.cCWSignal = false;
            }

            if (proximitySensor != null)
                proximitySensor.SetState(objectDetected);

            if (motorStatusRenderer != null)
                motorStatusRenderer.material.color = motorOn ? Color.green : Color.gray;

            if (objectSensorRenderer != null)
                objectSensorRenderer.material.color = objectDetected ? Color.red : Color.gray;

            if (dashboard != null)
                dashboard.Apply(message, _client.IsConnected);
        }

        void RotateMotorVisual()
        {
            if (motorRotor == null || _latestMessage == null || _latestMessage.measurements == null)
                return;

            float rpm = _latestMessage.measurements.rpm;
            motorRotor.Rotate(Vector3.forward, -rpm * motorRotationScale * Time.deltaTime, Space.Self);
        }

        private void OnDestroy()
        {
            _client.Dispose();
        }
    }
}
