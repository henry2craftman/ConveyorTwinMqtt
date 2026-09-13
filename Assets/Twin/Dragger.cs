using UnityEngine;

/// <summary>
/// 1. PLC신호를 받으면, 목적지 까지 이동 후, 끝까지 이동하면 원위치로 돌아온다.
/// 2. 플라스틱 or 금속 태그가 붙은 물체가 감지되면, 해당 물체를 자식으로 만들고
/// 속성: 원래위치, 목적지
/// </summary>
public class Dragger : MonoBehaviour
{
    public Transform cWDestination; // 시계방향으로 신호를 받을 때의 목적지
    public Transform cCWDestination;
    Vector3 dir;

    public void Move(bool isCW,  float speed)
    {
        if(isCW)
        {
            dir = cWDestination.position - transform.position;
            float distance = dir.magnitude;

            if (distance < 0.1f)
            {
                transform.position = cCWDestination.position;
            }
        }
        else
        {
            dir = cCWDestination.position - transform.position;
            float distance = dir.magnitude;

            if(distance < 0.1f)
            {
                transform.position = cWDestination.position;
            }
        }

        transform.position += dir.normalized * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        //if (other.tag == "플라스틱" || other.tag == "금속")
        {
            other.transform.SetParent(this.transform); // 컨베이어에 부딪힌 물체를 자식으로!
        }
    }

    private void OnTriggerExit(Collider other)
    {
        //if (other.tag == "플라스틱" || other.tag == "금속")
        {
            other.transform.SetParent(null); // 부모로 부터 해방
        }
    }
}
