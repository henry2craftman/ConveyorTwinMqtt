using UnityEngine;

namespace MPS
{
    /// <summary>
    /// 컨베이어 근접센서 Trigger용 컴포넌트.
    /// Trigger Collider가 있는 센서 GameObject에 붙이고, controller를 UnityAConveyorMockController로 연결합니다.
    /// </summary>
    public class ConveyorProximitySensor : MonoBehaviour
    {
        public UnityAConveyorMockController controller;
        public Sensor visualSensor;
        public string requiredTag;
        public bool clearOnExit;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsAllowed(other))
                return;

            if (controller != null)
                controller.SetObjectDetected(true);

            if (visualSensor != null)
                visualSensor.SetState(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!clearOnExit || !IsAllowed(other))
                return;

            if (controller != null)
                controller.SetObjectDetected(false);

            if (visualSensor != null)
                visualSensor.SetState(false);
        }

        bool IsAllowed(Collider other)
        {
            return string.IsNullOrWhiteSpace(requiredTag) || other.CompareTag(requiredTag);
        }
    }
}
