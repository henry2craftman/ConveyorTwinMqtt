using System;

namespace MPS
{
    /// <summary>
    /// PLC/MxComponent/Mock PLC에서 나온 컨베이어 상태를 MQTT로 전달하기 위한 공통 메시지.
    ///
    /// 교육용 Device Map
    ///   Y20 -> conveyorMotorOn   : PLC 출력, 컨베이어 모터 ON/OFF
    ///   X0  -> objectDetected    : PLC 입력, 근접센서 물체 감지
    ///   D100 -> temperatureC     : 온도
    ///   D110 -> vibrationMmS     : 진동
    ///   D120 -> currentA         : 전류
    ///   D130 -> rpm              : 모터 회전수
    /// </summary>
    [Serializable]
    public class ConveyorTwinMessage
    {
        public string schema = "factory.conveyor.state.v1";
        public string lineId = "line1";
        public string source = "unity-a-mock";
        public string mode = "mock";
        public string timestamp;
        public string quality = "good";
        public bool connected = true;
        public ConveyorTwinState state = new ConveyorTwinState();
        public ConveyorTwinMeasurements measurements = new ConveyorTwinMeasurements();
        public ConveyorTwinStatus status = new ConveyorTwinStatus();
        public ConveyorTwinRaw raw = new ConveyorTwinRaw();

        public static string NowIso()
        {
            return DateTimeOffset.Now.ToString("o");
        }
    }

    [Serializable]
    public class ConveyorTwinState
    {
        public bool startButton;
        public bool objectDetected;
        public bool conveyorMotorOn;
    }

    [Serializable]
    public class ConveyorTwinMeasurements
    {
        public float temperatureC;
        public float vibrationMmS;
        public float currentA;
        public float rpm;
    }

    [Serializable]
    public class ConveyorTwinStatus
    {
        public string level = "idle";
        public string message = "Conveyor is stopped";
    }

    [Serializable]
    public class ConveyorTwinRaw
    {
        public bool Y20;
        public bool X0;
        public int D100;
        public int D110;
        public int D120;
        public int D130;
    }
}
