using System.Collections.Generic;
using System.Linq;
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

		private List<Vector2> m_pathfindWaypoints;


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

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			m_aiState.DebugGizmo();

			if (ConsoleCommands.AIDebugLevel >= (int)ConsoleCommands.AIDebugLevels.Path && m_pathfindWaypoints?.Count > 0)
			{
				UnityEditor.Handles.DrawLine(transform.position, m_pathfindWaypoints.First());
				for (int i = 0, n = m_pathfindWaypoints.Count - 1; i < n; ++i)
				{
					UnityEditor.Handles.DrawLine(m_pathfindWaypoints[i], m_pathfindWaypoints[i + 1]);
				}
			}
		}
#endif


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
			const float arrivalEpsilon = 0.1f; // TODO: derive/calculate?

			// pathfind
			// TODO: efficiency?
			Vector2 targetPos = target == null ? transform.position : target.position + (Vector3)(transform.position.x > target.position.x ? targetOffsetAbs : targetOffsetAbs * new Vector2(-1.0f, 1.0f));
			if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0 || Vector2.Distance(targetPos, m_pathfindWaypoints.Last()) > m_meleeRange) // TODO: better re-plan trigger(s)?
			{
				m_pathfindWaypoints = Camera.main.GetComponent<GameController>().Pathfind(transform.position, targetPos);
				if (m_pathfindWaypoints == null)
				{
					// TODO: handle unreachable positions; find closest reachable position?
					m_pathfindWaypoints = new List<Vector2> { targetPos };
				}
			}
			Vector2 nextWaypoint = m_pathfindWaypoints.First();

			// left/right
			Vector2 diff = nextWaypoint - (Vector2)transform.position;
			bool hasArrivedX = Mathf.Abs(diff.x) <= collider2d.bounds.extents.x + Mathf.Abs(collider2d.offset.x) + arrivalEpsilon;
			move.x = hasArrivedX ? 0.0f : Mathf.Clamp(diff.x, -1.0f, 1.0f); // TODO: less slow-down when near waypoints

			if (HasFlying)
			{
				// fly
				move.y = Mathf.Clamp(diff.y, -1.0f, 1.0f); // TODO: less slow-down when near waypoints
			}
			else
			{
				// jump/drop
				// TODO: actual room-based pathfinding to avoid getting stuck
				Collider2D targetCollider = target == null ? null : target.GetComponent<Collider2D>();
				Bounds nextBounds = targetCollider == null || m_pathfindWaypoints.Count > 1 ? new(nextWaypoint, Vector3.zero) : targetCollider.bounds;
				Bounds selfBounds = collider2d.bounds;
				if (IsGrounded && nextBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
				{
					jump = true;
				}
				move.y = nextBounds.max.y < selfBounds.min.y ? -1.0f : 0.0f;
			}

			if (hasArrivedX && Mathf.Abs(diff.y) <= collider2d.bounds.extents.y + Mathf.Abs(collider2d.offset.y) + arrivalEpsilon)
			{
				m_pathfindWaypoints.RemoveAt(0);
			}
			return m_pathfindWaypoints.Count == 0;
		}
	}
}
