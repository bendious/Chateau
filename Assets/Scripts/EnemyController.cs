using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// A simple controller for enemies. Provides movement control toward a target object.
/// </summary>
public class EnemyController : KinematicCharacter
{
	public Vector2 m_targetOffset = Vector2.zero;
	public Transform m_target;

	public Vector2 m_armOffset; // TODO: combine w/ AvatarController version?

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

	protected override float IntegrateForcedVelocity(float target, float forced)
	{
		return Mathf.Abs(forced) < 0.01f ? target : forced;
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
		if (MaxPickUps > 0)
		{
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			if (arms.Length > 0)
			{
				for (int i = 0; i < arms.Length; ++i)
				{
					arms[i].UpdateAim(m_armOffset, m_target.position);
				}
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
			int i = 0;
			int[] lineIndicies = m_pathfindWaypoints.SelectMany(vec2 => new int[] { i, ++i }).ToArray()[0 .. ^2]; // i.e. [0, 1, 1, 2, 2, 3, ..., WaypointCount - 2, WaypointCount - 1]
			UnityEditor.Handles.DrawLines(m_pathfindWaypoints.Select(vec2 => (Vector3)vec2).ToArray(), lineIndicies);
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
		if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0 || Vector2.Distance(target.position, m_pathfindWaypoints.Last()) > targetOffsetAbs.magnitude + m_meleeRange) // TODO: better re-plan trigger(s)? avoid trying to go past moving targets?
		{
			m_pathfindWaypoints = Camera.main.GetComponent<GameController>().Pathfind(transform.position, target.position, targetOffsetAbs);
			if (m_pathfindWaypoints == null)
			{
				// TODO: handle unreachable positions; find closest reachable position?
				m_pathfindWaypoints = new List<Vector2> { target.position };
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
			// TODO: avoid getting stuck on corners?
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
