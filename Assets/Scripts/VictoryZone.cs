using UnityEngine;


public class VictoryZone : MonoBehaviour
{
	private void OnTriggerEnter2D(Collider2D collision)
	{
		if (collision.GetComponent<AvatarController>() == null)
		{
			return;
		}
		GameController.Instance.OnVictory();
	}
}
