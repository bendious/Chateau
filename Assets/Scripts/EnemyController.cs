using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// A simple controller for enemies. Provides movement control toward a target object.
	/// </summary>
	public class EnemyController : AnimationController
	{
		public Vector2 m_targetOffset = Vector2.zero;
		public Transform m_target;

		public float m_meleeRange = 1.0f;


		private AIState m_aiState;


		protected override void Start()
		{
			base.Start();
			m_aiState = new AIPursue(this);
			m_aiState.Enter();
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
				AIState stateNew = m_aiState.Update();
				if (stateNew != null)
				{
					// TODO: split across frames?
					m_aiState.Exit();
					m_aiState = stateNew;
					m_aiState.Enter();
				}
			}

			base.Update();
		}

		protected override void FixedUpdate()
		{
			base.FixedUpdate();

			// aim items
			if (m_maxPickUps > 0 && transform.childCount > 0)
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


		// TODO: un-expose?
		public bool NavigateTowardTarget(Transform target, Vector2 targetOffsetAbs)
		{
			// left/right
			Vector3 targetPos = target == null ? transform.position : target.position + (Vector3)(transform.position.x > target.position.x ? targetOffsetAbs : targetOffsetAbs * new Vector2(-1.0f, 1.0f)); // TODO: handle pathfinding / unreachable positions
			bool hasArrivedX = Mathf.Abs(targetPos.x - transform.position.x) < collider2d.bounds.extents.x;
			move.x = hasArrivedX ? 0.0f : Mathf.Clamp(targetPos.x - transform.position.x, -1.0f, 1.0f);

			if (HasFlying)
			{
				// fly
				move.y = Mathf.Clamp(targetPos.y - transform.position.y, -1.0f, 1.0f);
			}
			else
			{
				// jump/drop
				// TODO: actual room-based pathfinding to avoid getting stuck
				Collider2D targetCollider = target?.GetComponent<Collider2D>();
				Bounds targetBounds = targetCollider == null ? new Bounds(targetPos, Vector3.zero) : targetCollider.bounds;
				Bounds selfBounds = collider2d.bounds;
				if (IsGrounded && targetBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
				{
					jump = true;
				}
				move.y = targetBounds.max.y < selfBounds.min.y ? -1.0f : 0.0f;
			}

			return hasArrivedX && Mathf.Abs(targetPos.y - transform.position.y) < collider2d.bounds.extents.y;
		}
	}
}
