using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


namespace AStar_Test
{
    /// <summary>
    /// ���݂̃m�[�h�̏��
    /// </summary>
    public enum Status { None, Open, Closed , Wall, Target , Start}
    /// <summary>
    /// �ړ��R�X�g
    /// </summary>
    public enum Cost { Start = 0, Ground = 1 }
    /// <summary>
    /// ���m�E�ړ�����
    /// </summary>
    public enum Direction { r=0, d, l, u, rd, ld, lu, ru }
    /// <summary>
    /// �ړ������@(�ǉ��\��)
    /// </summary>
    public enum MoveDirection { Fore = 4 }
    /// <summary>
    /// �ړ����@�@���`��� or ���W�x�^����
    /// </summary>
    public enum MoveType { Lerp, Warp }

    public static class Field
    {
        public struct Node
        {
            public int actualCost;          // ���R�X�g�@ (�ړ��R�X�g�@������ړ����ɂ����Ȃ�)
            public double estimatedCost;    // ����R�X�g (�ڕW�܂ł̋���)
            public double score;            // �X�R�A     (���R�X�g + ����R�X�g)
            public Status status;           // �m�[�h�̏��
        }

        /// <summary>
        /// ����8������Point���擾
        /// </summary>
        /// <param name="vec"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        public static Vector2Int MovePosition(this Vector2Int vec, int n)
        {
            switch (n)
            {
                case (int)Direction.r:
                    vec.x += 1;
                    return vec;
                case (int)Direction.rd:
                    vec.x += 1; vec.y -= 1;
                    return vec;
                case (int)Direction.d:
                    vec.y -= 1;
                    return vec;
                case (int)Direction.ld:
                    vec.x -= 1; vec.y -= 1;
                    return vec;
                case (int)Direction.l:
                    vec.x -= 1;
                    return vec;
                case (int)Direction.lu:
                    vec.x -= 1; vec.y += 1;
                    return vec;
                case (int)Direction.u:
                    vec.y += 1;
                    return vec;
                case (int)Direction.ru:
                    vec.x += 1; vec.y += 1;
                    return vec;

                default: return Vector2Int.zero;
            }
        }

        /// <summary>
        /// Vector3 ���� Vector2Int �֕ϊ�
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static Vector2Int ToPoint(this Vector3 pos) =>
            new Vector2Int { x = (int)Mathf.Floor(pos.x), y = (int)Mathf.Floor(pos.y) };
        public static Vector2Int ToPoint(int x, int y) => new Vector2Int(x, y);

        /// <summary>
        /// Vector2Int ���� Vector3 �֕ϊ�
        /// </summary>
        /// <param name="vec"></param>
        /// <returns></returns>
        public static Vector3 ToPosition(this Vector2Int vec) => new Vector3(vec.x, vec.y, 0);

        public static double ToDistance(Vector2Int a, Vector2Int b)
            => Math.Sqrt(Math.Pow(a.x - b.x, 2) + Math.Pow(a.y - b.y, 2));
    }



    public class AStarTestManager : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, Field.Node> _node = new Dictionary<Vector2Int, Field.Node>();
        private Vector2Int _startPosition, _targetPosition;              // �J�n�n�_�E�ڕW�n�_
        private List<Vector2Int> _root = new List<Vector2Int>();         // �ŒZ�o�H
        private List<Vector2Int> _branchPoint = new List<Vector2Int>();  // ����n�_
        private double _hashScore = 0;                                   // �X�R�A(�ꎞ�ۑ�)
        private int _attemptsCount = 0;                                  // ���s��
        private Status _targetStatus = Status.Target;                    // �ڕW�n�_�̃X�e�[�^�X

        [SerializeField] int line = 8;                      // Y-axis  �s
        [SerializeField] int column = 15;                   // X-axis  ��
        [SerializeField] GameObject _targetObj;             // �ڕW�ƂȂ�I�u�W�F�N�g
        [SerializeField] GameObject _wall;                  // �ǂ��q�I�u�W�F�N�g�Ɏ���
        [SerializeField] Button _startButton;               // �J�n�{�^���@(�{�^���R���|�[�l���g�͕ύX���Ȃ��Ă悢)
        [SerializeField] int _maxAttempts = 100;            // �ő厎�s��
        [SerializeField] float _searchDelayTime = .25f;     // �o�H�T�����̒x������
        [SerializeField] float _traceDelayTime = .15f;      // �ŒZ�o�H�T�����̒x������
        [Space(20)]
        [SerializeField] MoveDirection _moveDirection;      // �ړ�����
        [SerializeField] MoveType _moveType;                // �ړ����@

        private void Awake() => _startButton.onClick.AddListener(Init);

        private void Init()
        {
            // �m�[�h������
            for (int x = 0; x <= column; x++)
            {
                for (int y = 0; y <= line; y++)
                {
                    if (!_node.ContainsKey(Field.ToPoint(x, y)))
                        _node.Add(Field.ToPoint(x, y), new Field.Node()
                        {
                            actualCost = (int)Cost.Ground,
                            estimatedCost = 0,
                            score = 0,
                            status = Status.None
                        }
                        );
                }
            }

            // �J�n�n�_�擾
            _startPosition = transform.position.ToPoint();
            _node[_startPosition] = new Field.Node()
            {
                actualCost = (int)Cost.Ground,
                status = Status.Start
            };

            // �ڕW�n�_�擾
            _targetPosition = _targetObj.transform.position.ToPoint();
            _node[_targetPosition] = new Field.Node()
            {
                actualCost = (int)Cost.Ground,
                status = Status.Target
            };

            // �ǐݒ�
            for (int i = 0; i < _wall.transform.childCount; i++)
            {
                var pos = _wall.transform.GetChild(i).position.ToPoint();
                _node[pos] = new Field.Node() { status = Status.Wall };
            }

            Open();
        }

        /// <summary>
        /// ���͂̃X�R�A�����߂�
        /// </summary>
        private void Open()
        {
            // ���s�񐔂��ő厎�s�񐔂𒴂����ꍇ
            if (++_attemptsCount > _maxAttempts)
            {
                Debug.LogError("Overflow: ���s�񐔂��ő厎�s�񐔂𒴂��܂���");
                UnityEditor.EditorApplication.isPaused = true;
                return;
            }

                // debug
                //print(_attemptsCount + "���");
            var around = new Dictionary<Vector2Int, double>();

            // ���͂̍ŏ��X�R�A�����߂�
            for (var i = 0; i < (int)_moveDirection; i++)
            {
                Vector2Int pos = transform.position.ToPoint().MovePosition(i);
                if (pos.x < 0 || pos.y < 0 || pos.x > column || pos.y > line) continue;
                else if (_node[pos].status == Status.Wall) continue;
                else if (_node[pos].status == Status.Closed) continue;
                else if (_node[pos].status == (_targetStatus == Status.Start ? Status.Target : Status.Start)) continue;

                // �^�[�Q�b�g����������v�Z�I��
                if (_node[pos].status == _targetStatus)
                {
                    _hashScore = 0;
                    Move(pos, _targetStatus);
                    return;
                }

                // ����R�X�g�@�v�Z
                var estimated = Field.ToDistance(_targetPosition, pos);

                var hashStatus = _node[pos].status;
                // �X�R�A�@�v�Z
                _node[pos] = new Field.Node()
                {
                    status = hashStatus,
                    actualCost = (int)Cost.Ground + _attemptsCount,
                    estimatedCost = estimated,
                    score = (int)Cost.Ground + _attemptsCount + estimated
                };

                // �X�R�A���Ȃ�(Open���Ă��Ȃ�)�ꍇ�͍l�����Ȃ�
                if (_node[pos].score <= 0)
                {
                    continue;
                }
                around.Add(pos,  _node[pos].score);
            }

            // ����n�_��ێ����Ă���
            if (around.Count > 1)
            {
                _branchPoint.Add(transform.position.ToPoint());
            }
            // �����ړ��\�n�_���Ȃ���Ε���n�_�ɖ߂�
            else if (around.Count == 0)
            {
                if(_branchPoint.Count == 0)
                {
                    Debug.LogError("Overflow: �ʘH�𔭌����邱�Ƃ��ł��܂���ł���");
                    UnityEditor.EditorApplication.isPaused = true;
                    return;
                }
                transform.position = _branchPoint[_branchPoint.Count - 1].ToPosition();
                _branchPoint.Remove(_branchPoint[_branchPoint.Count - 1]);
                Open();
                return;
            }

            // �ŏ��R�X�g�̍��W
            var minCost = around.OrderBy(p => p.Value).First();
            _hashScore = minCost.Value;

            Move(minCost.Key, _targetStatus);
        }

        /// <summary>
        /// �ړ�
        /// </summary>
        /// <param name="nextPoint"></param>
        /// <param name="targetStatus"></param>
        private void Move(Vector2Int nextPoint, Status targetStatus)
        {
            Closed(transform.position.ToPoint());

            transform.position = new Vector3(nextPoint.x, nextPoint.y, 0);
            if (_node[nextPoint].status == targetStatus)
            {
                StartCoroutine(Wait(_traceDelayTime, () =>
                {
                    if (targetStatus == Status.Target)
                        GetMinCostCoordinate();
                }));
                return;
            }

            StartCoroutine(Wait(_searchDelayTime, () => Open()));
        }

        /// <summary>
        /// �ʂ��Ă������������Ԃ��Ȃ����߂ɕ���
        /// </summary>
        /// <param name="beforePoint"></param>
        private void Closed(Vector2Int beforePoint)
        {
            var hashStatus = beforePoint==_startPosition ? Status.Start : Status.Closed;
            var hashActual = _node[beforePoint].actualCost;
            _node[beforePoint] = new Field.Node() {  actualCost = hashActual, score = _hashScore, status = hashStatus };
        }

        /// <summary>
        /// �S�[������t�Z���čŒZ���[�g�����߂�
        /// </summary>
        /// <param name="val"></param>
        private void GetMinCostCoordinate()
        {
            var isBreak = false; ;
            // �ŒZ�o�H�̌���
            _root.Add(transform.position.ToPoint());

            // Target�̒n�_��o�^
            var minCost = new KeyValuePair<Vector2Int, double>();

            int n = 0;

            // Start�܂ł̍ŒZ�o�H������
            do
            {
                var closed = new Dictionary<Vector2Int, double>();
                for (var i = 0; i < (int)_moveDirection; i++)
                {
                    Vector2Int pos = _root[_root.Count-1].MovePosition(i);
                    if (pos.x < 0 || pos.y < 0 || pos.x > column || pos.y > line) continue;
                    else if (_node[pos].status == Status.Start) 
                    {
                        isBreak = true; 
                        break; 
                    }
                    else if (_node[pos].status == Status.Wall) continue;
                    else if (_node[pos].status != Status.Closed || pos == _root[_root.Count - 1]) continue;
                    // ���R�X�g�����������̂�T��
                    closed.Add(pos, _node[pos].actualCost);
                }

                if (isBreak) break;
                // �ŏ����R�X�g�̍��W
                minCost = closed.OrderBy(p => p.Value).ThenBy(p => Field.ToDistance(p.Key, _startPosition)).First();

                _root.Add(minCost.Key);
            } while (n++ < _maxAttempts);

            _root.Add(_startPosition);
            transform.position = _startPosition.ToPosition();
            // �ŒZ�o�H�̌����@�I��

            // "�^�[�Q�b�g->�X�^�[�g" ���� "�X�^�[�g->�^�[�Q�b�g" �ɒ���
            _root.Reverse();

            TraceRoot(_root);
        }

        private void TraceRoot(List<Vector2Int> root)
        {
            StartCoroutine(Trace(root));
        }

        /// <summary>
        /// �ŒZ�o�H���ړ�
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        private IEnumerator Trace(List<Vector2Int> root)
        {
            if (_moveType == MoveType.Warp)
            {
                foreach (var r in root)
                {
                    transform.position = r.ToPosition();
                    yield return new WaitForSeconds(_traceDelayTime);
                }
            }
            else if(_moveType == MoveType.Lerp)
            {
                for(int i = 0; i < root.Count-1; i++)
                {
                    float t = 0;

                    while (t < 1)
                    {
                        t += Time.deltaTime * (1 / _traceDelayTime);
                        transform.position = Vector2.Lerp(root[i], root[i + 1], t);
                        yield return null;
                    }
                }
            }

            print("GOAL!!");
        }

        public IEnumerator Wait(float time, Action action)
        {
            yield return new WaitForSeconds(time);
            action();
        }
    }
}