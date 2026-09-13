using UnityEngine;

/// <summary>
/// PLC의 출력신호 Y00(정방향회전), Y01(역방향회전) 신호를 받아 
/// 컨베이어를 움직인다(Dragger들을 특정 속도로 이동시킨다.)
/// 속성: 정방향 신호, 역방향 신호, Dragger 리스트, 컨베이어 속도
/// </summary>
public class Conveyor : MonoBehaviour
{
    [Header("PLC 신호들")]
    public bool cWSignal;
    public bool cCWSignal;

    [Header("컨베이어 세팅")]
    public Dragger[] draggers;
    public float speed;

    // 0.02초의 고정 프레임 속도로 작동
    void FixedUpdate()
    {
        if(cWSignal)
        {
            foreach (var dragger in draggers)
            {
                dragger.Move(cWSignal, speed);
            }
        }
        else if(cCWSignal)
        {
            foreach (var dragger in draggers)
            {
                dragger.Move(!cCWSignal, speed);
            }
        }
    }
}
