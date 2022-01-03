using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	public class EnemyController : AnimationController
	{
		public Transform m_target;


		void OnCollisionEnter2D(Collision2D collision)
		{
			var avatar = collision.gameObject.GetComponent<AvatarController>();
			if (avatar != null)
			{
				avatar.OnCollision(this);
			}
		}

		protected override void Update()
		{
			// left/right
			AvatarController targetAvatar = m_target.GetComponent<AvatarController>();
			bool moveAway = targetAvatar != null && !targetAvatar.controlEnabled; // avoid softlock from enemies in spawn position // TODO: better shouldMoveAway flag?
			move.x = m_target == null ? 0.0f : Mathf.Clamp((m_target.position.x - transform.position.x) * (moveAway ? -1.0f : 1.0f), -1.0f, 1.0f);

			// jump/drop
			// TODO: actual pathfinding
			Bounds targetBounds = targetAvatar.GetComponent<CircleCollider2D>().bounds;
			Bounds selfBounds = GetComponent<CapsuleCollider2D>().bounds;
			if (IsGrounded && targetBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
			{
				jump = true;
			}
			else if (targetBounds.max.y < selfBounds.min.y)
			{
				move.y = -1.0f;
			}

			base.Update();
		}

		public override void OnDeath()
		{
			base.OnDeath();
			enabled = false;
		}

		protected override void DespawnSelf()
		{
			Camera.main.GetComponent<GameController>().OnEnemyDespawn(this);
			base.DespawnSelf();
		}
	}
}
