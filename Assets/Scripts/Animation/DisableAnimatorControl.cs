using UnityEngine;


public class DisableAnimatorControl : StateMachineBehaviour
{
	private AvatarController m_avatar;
	private EnemyController m_enemy;

	private bool m_wasPassive;


	// OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
	override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		m_avatar = animator.GetComponent<AvatarController>();
		if (m_avatar != null)
		{
			m_avatar.DeactivateAllControl();
		}
		m_enemy = animator.GetComponent<EnemyController>();
		if (m_enemy != null)
		{
			m_wasPassive = m_enemy.m_passive;
			m_enemy.m_passive = true;
		}
	}

	// OnStateExit is called when a transition ends and the state machine finishes evaluating this state
	override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		if (m_avatar != null)
		{
			m_avatar.EnablePlayerControl();
		}
		if (m_enemy != null)
		{
			m_enemy.m_passive = m_wasPassive;
		}
	}
}
