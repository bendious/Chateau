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
			var avatar = collision.gameObject.GetComponent<AvatarController>();
			if (avatar != null)
			{
				var evt = Schedule<AvatarEnemyCollision>();
				evt.avatar = avatar;
				evt.enemy = this;
			}
		}

		protected override void Update()
		{
			AvatarController targetAvatar = m_target.GetComponent<AvatarController>();
			bool moveAway = targetAvatar != null && !targetAvatar.controlEnabled; // avoid softlock from enemies in spawn position // TODO: better shouldMoveAway flag?
			move.x = m_target == null ? 0.0f : Mathf.Clamp((m_target.position.x - transform.position.x) * (moveAway ? -1.0f : 1.0f), -m_moveMax, m_moveMax);
			base.Update();
		}
	}
}
