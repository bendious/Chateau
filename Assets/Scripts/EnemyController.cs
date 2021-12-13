using Platformer.Gameplay;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	public class EnemyController : AnimationController
	{
		public Transform m_target;
		public float m_moveMax = 0.1f;


		void OnCollisionEnter2D(Collision2D collision)
		{
			var player = collision.gameObject.GetComponent<PlayerController>();
			if (player != null)
			{
				var ev = Schedule<PlayerEnemyCollision>();
				ev.player = player;
				ev.enemy = this;
			}
		}

		protected override void Update()
		{
			PlayerController targetPlayer = m_target.GetComponent<PlayerController>();
			bool moveAway = targetPlayer != null && !targetPlayer.controlEnabled; // avoid softlock from enemies in spawn position // TODO: better shouldMoveAway flag?
			move.x = m_target == null ? 0.0f : Mathf.Clamp((m_target.position.x - transform.position.x) * (moveAway ? -1.0f : 1.0f), -m_moveMax, m_moveMax);
			base.Update();
		}
	}
}
