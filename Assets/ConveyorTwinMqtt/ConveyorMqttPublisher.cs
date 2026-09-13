using System;
using Newtonsoft.Json;
using UnityEngine;

namespace MPS
{
    /// <summary>
    /// Unity A에서 Mock PLC 또는 현장 Twin 상태를 MQTT로 발행합니다.
    /// 기본 topic: factory/line1/conveyor/state
    /// </summary>
    public class ConveyorMqttPublisher : MonoBehaviour
    {
        [Header("상태 공급자")]
        public UnityAConveyorMockController mockController;

        [Header("MQTT Broker")]
        public string brokerHost = "127.0.0.1";
        public int brokerPort = 1883;
        public string clientId = "unity-a-conveyor-publisher";
        public string stateTopic = ConveyorMqttTopics.State;
        public string rawTopic = ConveyorMqttTopics.Raw;
        public string heartbeatTopic = ConveyorMqttTopics.Heartbeat;
        public bool connectOnStart = true;

        [Header("발행 설정")]
        public float publishInterval = 0.2f;
        public bool publishRawTopic = true;
        public bool logPublishedJson;

        readonly MqttLiteClient _client = new MqttLiteClient();
        float _nextPublishTime;
        bool _isConnecting;
        bool _isPublishing;

        public bool IsConnected => _client.IsConnected;

        private async void Start()
        {
            _client.Error += ex => Debug.LogWarning($"[MQTT Publisher] {ex.Message}");

            if (connectOnStart)
                await ConnectAsync();
        }

        private void Update()
        {
            if (!_client.IsConnected || _isPublishing || Time.time < _nextPublishTime)
                return;

            _nextPublishTime = Time.time + publishInterval;
            _ = PublishStateAsync();
        }

        public async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_isConnecting || _client.IsConnected)
                return;

            _isConnecting = true;
            try
            {
                await _client.ConnectAsync(brokerHost, brokerPort, clientId);
                Debug.Log($"[MQTT Publisher] Connected to {brokerHost}:{brokerPort}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQTT Publisher] Connect failed: {ex.Message}");
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public async System.Threading.Tasks.Task PublishStateAsync()
        {
            if (mockController == null || !_client.IsConnected)
                return;

            _isPublishing = true;
            try
            {
                ConveyorTwinMessage message = mockController.GetSnapshot();
                string json = JsonConvert.SerializeObject(message, Formatting.None);
                await _client.PublishAsync(stateTopic, json);

                if (publishRawTopic)
                {
                    string rawJson = JsonConvert.SerializeObject(new
                    {
                        schema = "factory.conveyor.raw.v1",
                        lineId = message.lineId,
                        source = message.source,
                        timestamp = message.timestamp,
                        quality = message.quality,
                        devices = message.raw,
                        scaling = new
                        {
                            D100_temperatureC = "raw / 10",
                            D110_vibrationMmS = "raw / 10",
                            D120_currentA = "raw / 10",
                            D130_rpm = "raw"
                        }
                    }, Formatting.None);

                    await _client.PublishAsync(rawTopic, rawJson);
                }

                if (logPublishedJson)
                    Debug.Log($"[MQTT Publisher] {stateTopic} {json}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQTT Publisher] Publish failed: {ex.Message}");
            }
            finally
            {
                _isPublishing = false;
            }
        }

        public async void PublishHeartbeat()
        {
            if (!_client.IsConnected)
                return;

            string payload = JsonConvert.SerializeObject(new
            {
                schema = "factory.conveyor.heartbeat.v1",
                source = clientId,
                timestamp = ConveyorTwinMessage.NowIso(),
                connected = true
            });

            await _client.PublishAsync(heartbeatTopic, payload);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        private void OnDestroy()
        {
            _client.Dispose();
        }
    }
}
