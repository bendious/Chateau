using Platformer.Mechanics;
using UnityEngine;


public class VictoryZone : MonoBehaviour
{
	private void OnTriggerEnter2D(Collider2D collision)
	{
		if (collision.GetComponent<AvatarController>() == null)
		{
			return;
		}
		GameController game = Camera.main.GetComponent<GameController>();
		if (game.EnemiesRemain())
		{
			return;
		}
		game.OnVictory();
	}
}
