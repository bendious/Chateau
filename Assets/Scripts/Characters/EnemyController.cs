using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// A simple controller for enemies. Provides movement control toward a target object.
/// </summary>
[DisallowMultipleComponent]
public class EnemyController : KinematicCharacter
{
	public AIState.Type[] m_allowedStates = new AIState.Type[] { AIState.Type.Pursue, AIState.Type.Flee, AIState.Type.RamSwoop };

	public float m_contactDamage = 1.0f;

	public Vector2 m_targetOffset = Vector2.zero;
	public Transform m_target;
	[SerializeField]
	private float m_replanSecondsMax = 2.0f;

	public float m_meleeRange = 1.0f;

	public float m_dropDecayTime = 0.2f;

	public AudioClip[] m_attackSFX; // TODO: remove in favor of animation triggers w/ AudioCollection?
	public WeightedObject<GameObject>[] m_teleportVFX;


	[SerializeField]
	private GameObject m_heldPrefab;


	private float m_targetSelectTimeNext;
	private AIState m_aiState;

	private float m_pathfindTimeNext;
	private List<Vector2> m_pathfindWaypoints;

	private float m_dropDecayVel;


	protected override void Start()
	{
		base.Start();
		// TODO: spawn animation / fade-in?
		m_targetSelectTimeNext = Time.time + Random.Range(0.0f, m_replanSecondsMax);
		m_pathfindTimeNext = Time.time + Random.Range(0.0f, m_replanSecondsMax);

		if (m_heldPrefab == null)
		{
			return;
		}

		foreach (ArmController arm in GetComponentsInChildren<ArmController>())
		{
			if (arm.transform.childCount > 0)
			{
				return;
			}
			arm.ChildAttach(GameController.Instance.m_savableFactory.Instantiate(m_heldPrefab, transform.position, transform.rotation).GetComponent<ItemController>());
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		var avatar = collision.collider.GetComponent<AvatarController>(); // NOTE that we use the collider object rather than collision.gameObject since w/ characters & arms, they are not always the same
		if (avatar != null)
		{
			avatar.OnEnemyCollision(this);
		}
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		var avatar = collision.collider.GetComponent<AvatarController>(); // NOTE that we use the collider object rather than collision.gameObject since w/ characters & arms, they are not always the same
		if (avatar != null)
		{
			avatar.OnEnemyCollision(this);
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
			if (m_aiState == null)
			{
				m_aiState = AIState.FromTypePrioritized(m_allowedStates, this);
				m_aiState.Enter();
			}

			AIState stateNew = m_aiState.Update();
			if (stateNew != m_aiState)
			{
				// TODO: split across frames?
				m_aiState.Exit();
				m_aiState = stateNew;
				if (m_aiState != null)
				{
					m_aiState.Enter();
				}
			}
		}

		base.Update();
	}

	protected override void FixedUpdate()
	{
		base.FixedUpdate();

		// aim items
		if (HoldCountMax > 0)
		{
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			if (arms.Length > 0)
			{
				ArmController primaryArm = arms.FirstOrDefault(arm => arm.GetComponentInChildren<ItemController>() != null);
				Vector2 targetPosSafe = m_target == null ? (Vector2)transform.position + (LeftFacing ? Vector2.left : Vector2.right) : m_target.position;
				if (primaryArm != null)
				{
					primaryArm.UpdateAim(m_armOffset, targetPosSafe, targetPosSafe);
				}

				int i = primaryArm == null ? -1 : 0;
				foreach (ArmController arm in arms)
				{
					if (arm == primaryArm)
					{
						continue;
					}
					Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, ++i * System.Math.Min(60, 360 / arms.Length)) * (targetPosSafe - (Vector2)transform.position);
					arm.UpdateAim(m_armOffset, aimPos, targetPosSafe);
				}
			}
		}
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_aiState != null)
		{
			m_aiState.DebugGizmo();
		}

		if (ConsoleCommands.AIDebugLevel >= (int)ConsoleCommands.AIDebugLevels.Path && m_pathfindWaypoints?.Count > 0)
		{
			UnityEditor.Handles.DrawLine(transform.position, m_pathfindWaypoints.First());
			int i = 0;
			int[] lineIndicies = m_pathfindWaypoints.SelectMany(vec2 => new int[] { i, ++i }).ToArray()[0 .. ^2]; // i.e. [0, 1, 1, 2, 2, 3, ..., WaypointCount - 2, WaypointCount - 1]
			UnityEditor.Handles.DrawLines(m_pathfindWaypoints.Select(vec2 => (Vector3)vec2).ToArray(), lineIndicies);
		}
	}
#endif


	public override void OnDamage(GameObject source)
	{
		base.OnDamage(source);

		AIState stateNew = m_aiState?.OnDamage(source);
		if (stateNew != m_aiState)
		{
			// NOTE that we can't split this across frames since we might not get another Update() call due to death
			m_aiState.Exit();
			m_aiState = stateNew;
			if (m_aiState != null)
			{
				m_aiState.Enter();
			}
		}
	}

	public override bool OnDeath()
	{
		if (!base.OnDeath())
		{
			return false;
		}

		enabled = false;

		return true;
	}


	// TODO: un-expose?
	public bool NavigateTowardTarget(Vector2 targetOffsetAbs)
	{
		if (m_contactDamage > 0.0f && m_targetSelectTimeNext <= Time.time && (m_target == null || m_target.GetComponent<AvatarController>() != null))
		{
			// choose appropriate avatar to target
			// TODO: use pathfind distances? allow re-targeting other types of targets?
			float sqDistClosest = float.MaxValue;
			foreach (AvatarController avatar in GameController.Instance.m_avatars)
			{
				if (!avatar.IsAlive)
				{
					continue;
				}
				Transform avatarTf = avatar.transform;
				float sqDist = Vector2.Distance(transform.position, avatarTf.position);
				if (sqDist < sqDistClosest)
				{
					sqDistClosest = sqDist;
					m_target = avatarTf;
				}
			}

			m_targetSelectTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_target == null)
		{
			move = Vector2.zero;
			return false; // TODO: flag to trigger idle behavior?
		}

		if (HasForcedVelocity)
		{
			move = Vector2.zero;
			return false;
		}

		// pathfind
		// TODO: efficiency?
		if (m_pathfindTimeNext <= Time.time || (m_pathfindWaypoints != null && m_pathfindWaypoints.Count > 0 && !Vector2.Distance(m_target.position, m_pathfindWaypoints.Last()).FloatEqual(targetOffsetAbs.magnitude, m_meleeRange))) // TODO: better re-plan trigger(s) (more precise as distance remaining decreases); avoid trying to go past moving targets?
		{
			m_pathfindWaypoints = GameController.Instance.Pathfind(transform.position, m_target.position, targetOffsetAbs);
			if (m_pathfindWaypoints == null)
			{
				m_target = null; // TODO: better handle unreachable positions; idle? find closest reachable position?
			}
			m_pathfindTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0)
		{
			move = Vector2.zero;
			return false;
		}
		Vector2 nextWaypoint = m_pathfindWaypoints.First();

		// determine current direction
		Vector2 diff = nextWaypoint - (Vector2)transform.position;
		Collider2D targetCollider = m_target.GetComponent<Collider2D>(); // TODO: efficiency?
		Vector2 halfExtentsCombined = (m_collider.bounds.extents + targetCollider.bounds.extents) * 0.5f;
		if (Mathf.Abs(diff.x) < halfExtentsCombined.x)
		{
			diff.x = 0.0f;
		}
		if (Mathf.Abs(diff.y) < halfExtentsCombined.y)
		{
			diff.y = 0.0f;
		}
		Vector2 dir = diff.normalized;

		// left/right
		move.x = HasFlying ? dir.x : System.Math.Sign(dir.x); // NOTE that Mathf's version of Sign() treats zero as positive...

		if (HasFlying)
		{
			// fly
			move.y = dir.y;
		}
		else
		{
			// jump/drop
			// TODO: only jump when directly below, but w/o getting stuck?
			Bounds nextBounds = targetCollider == null || m_pathfindWaypoints.Count > 1 ? new(nextWaypoint, Vector3.zero) : targetCollider.bounds;
			Bounds selfBounds = m_collider.bounds;
			if (IsGrounded && nextBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
			{
				m_jump = true;
			}
			move.y = IsGrounded && nextBounds.max.y < selfBounds.min.y ? -1.0f : Mathf.SmoothDamp(move.y, 0.0f, ref m_dropDecayVel, m_dropDecayTime * 2.0f); // NOTE the IsGrounded check and damped decay (x2 since IsDropping's threshold is -0.5) to cause stopping at each ladder rung when descending
		}

		const float arrivalEpsilon = 0.1f; // TODO: derive/calculate?
		if (diff.magnitude <= ((Vector2)m_collider.bounds.extents).magnitude + m_collider.offset.magnitude + arrivalEpsilon)
		{
			m_pathfindWaypoints.RemoveAt(0);
		}
		return m_pathfindWaypoints.Count == 0;
	}

	public void PlayAttackEffects()
	{
		animator.SetBool("attacking", true);

		if (m_attackSFX.Length > 0)
		{
			audioSource.PlayOneShot(m_attackSFX[Random.Range(0, m_attackSFX.Length)]);
		}
	}

	public void StopAttackEffects()
	{
		animator.SetBool("attacking", false);
		// TODO: cut off SFX if appropriate?
	}


	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void EnablePlayerControl()
	{
	}
}
