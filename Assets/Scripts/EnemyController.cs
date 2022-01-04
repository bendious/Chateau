using System.Linq;
using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	public class EnemyController : AnimationController
	{
		public float m_targetDistance = 0.0f;
		public Transform m_target;

		public bool m_itemCapability = false;


		protected override void Start()
		{
			base.Start();
			if (m_itemCapability)
			{
				IsPickingUp = true; // TODO: base on animation?
			}
		}

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
			if (ConsoleCommands.PassiveAI)
			{
				move = Vector2.zero;
			}
			else
			{
				// left/right
				AvatarController targetAvatar = m_target.GetComponent<AvatarController>();
				Vector3 targetPos = m_target == null ? transform.position : m_target.position + (transform.position - m_target.position).normalized * m_targetDistance;
				bool moveAway = targetAvatar != null && !targetAvatar.controlEnabled; // avoid softlock from enemies in spawn position // TODO: better shouldMoveAway flag?
				move.x = (targetPos - transform.position).magnitude < collider2d.bounds.extents.x ? 0.0f : Mathf.Clamp((targetPos.x - transform.position.x) * (moveAway ? -1.0f : 1.0f), -1.0f, 1.0f);

				// jump/drop
				// TODO: actual pathfinding
				Bounds targetBounds = targetAvatar.GetComponent<CircleCollider2D>().bounds;
				Bounds selfBounds = collider2d.bounds;
				if (IsGrounded && targetBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
				{
					jump = true;
				}
				else if (targetBounds.max.y < selfBounds.min.y)
				{
					move.y = -1.0f;
				}

				// swing
				if (m_itemCapability && transform.childCount > 0)
				{
					if (Random.value > 0.99f) // TODO
					{
						GetComponentInChildren<ItemController>().Swing();
					}

					// throw
					if (Random.value > 0.99f) // TODO
					{
						GetComponentInChildren<ItemController>().Throw();
					}
				}
			}

			base.Update();
		}

		protected override void FixedUpdate()
		{
			base.FixedUpdate();

			// aim items
			if (m_itemCapability && transform.childCount > 0)
			{
				Vector2 colliderSize = ((CapsuleCollider2D)collider2d).size;
				float holdRadius = Mathf.Max(colliderSize.x, colliderSize.y) * 0.5f;
				ItemController[] items = GetComponentsInChildren<ItemController>();
				for (int i = 0; i < items.Length; ++i)
				{
					items[i].UpdateAim(m_target.position, holdRadius);
				}
			}
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
