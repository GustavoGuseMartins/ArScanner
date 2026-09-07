using UnityEngine;

namespace ArScanner.Spatial
{
    /// <summary>
    /// Permite navegar livremente pela cena no Game View usando WASD + Botão Direito do Mouse durante o Play mode no PC.
    /// </summary>
    public class EditorCameraController : MonoBehaviour
    {
        [Header("Velocidades de Navegação")]
        public float moveSpeed = 3.0f;
        public float fastMultiplier = 2.5f;
        public float lookSensitivity = 2.0f;

        private float rotationX = 0f;
        private float rotationY = 0f;

        private void Start()
        {
            Vector3 angles = transform.eulerAngles;
            rotationX = angles.y;
            rotationY = angles.x;
        }

        private void Update()
        {
            // Ativa rotação apenas enquanto segurar o Botão Direito do Mouse
            if (Input.GetMouseButton(1))
            {
                Cursor.lockState = CursorLockMode.Confined;

                rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
                rotationY -= Input.GetAxis("Mouse Y") * lookSensitivity;
                rotationY = Mathf.Clamp(rotationY, -89f, 89f);

                transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);

                // Movimentação WASD + QE
                float currentSpeed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1.0f);

                Vector3 direction = new Vector3(
                    Input.GetAxisRaw("Horizontal"),
                    (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                    Input.GetAxisRaw("Vertical")
                );

                Vector3 movement = transform.TransformDirection(direction.normalized) * (currentSpeed * Time.deltaTime);
                transform.position += movement;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }
    }
}
