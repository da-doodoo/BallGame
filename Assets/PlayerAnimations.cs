using UnityEngine;

[RequireComponent(typeof(Animator), typeof(Rigidbody), typeof(Player))]
public class PlayerAnimator : MonoBehaviour
{
    public float dampTime = 0.1f;

    private Animator animator;
    private Rigidbody rb;
    private Player player;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        player = GetComponent<Player>();
    }

    void Update()
    {
        // Forward/backward speed relative to where the player faces, scaled to -1..1
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward) / player.MoveSpeed;

        animator.SetFloat("MoveY", forwardSpeed, dampTime, Time.deltaTime);
        animator.SetFloat("VelocityY", rb.linearVelocity.y);

        animator.speed = player.IsSprinting ? player.SprintSpeed / player.MoveSpeed : 1f;
        
        animator.SetBool("IsGrounded", player.IsGrounded);
    }
}