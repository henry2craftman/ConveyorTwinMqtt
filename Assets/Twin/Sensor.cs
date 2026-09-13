using UnityEngine;

namespace MPS
{
    /// <summary>
    /// 근접, 금속센서에 따라 물체가 감지되면 메시랜더러의 색상을 바꿔준다.
    /// ※가상의 센서를 PLC의 Input(X디바이스)으로 사용
    /// 속성: 센서타입 enum, PLC로 보내는 신호, 메시랜더러
    /// </summary>
    public class Sensor : MonoBehaviour
    {
        public enum SensorType
        {
            근접센서,
            금속감지센서,
            유동형센서
        }
        public SensorType sensorType = SensorType.근접센서;
        public bool sensorSignal;
        MeshRenderer mr;

        // PLC 실시간 연동 시 Trigger 대신 PLC 값으로 제어할지 여부
        [HideInInspector] public bool plcOverride;

        static readonly Color colorOn  = new Color(1, 0, 0, 0.7f);
        static readonly Color colorOff = new Color(0, 1, 0, 0.7f);

        private void Start()
        {
            mr = GetComponent<MeshRenderer>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (plcOverride) return;    // PLC 모드에서는 Trigger 무시

            if (sensorType == SensorType.금속감지센서)
            {
                if (other.tag == "금속")
                {
                    sensorSignal = true;
                    mr.material.color = colorOn;
                }
            }
            else
            {
                sensorSignal = true;
                mr.material.color = colorOn;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (plcOverride) return;

            sensorSignal = false;
            mr.material.color = colorOff;
        }

        /// <summary>
        /// PLC 입력값으로 센서 상태를 직접 설정 (디지털 트윈 연동용)
        /// MxPlcController에서 매 프레임 호출
        /// </summary>
        public void SetState(bool on)
        {
            sensorSignal = on;
            if (mr != null)
                mr.material.color = on ? colorOn : colorOff;
        }
    }
}
