using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


namespace AStar_Test
{
    /// <summary>
    /// 現在のノードの状態
    /// </summary>
    public enum Status { None, Open, Closed , Wall, Target , Start}
    /// <summary>
    /// 移動コスト
    /// </summary>
    public enum Cost { Start = 0, Ground = 1 }
    /// <summary>
    /// 検知・移動方向
    /// </summary>
    public enum Direction { r=0, d, l, u, rd, ld, lu, ru }
    /// <summary>
    /// 移動方向　(追加予定)
    /// </summary>
    public enum MoveDirection { Fore = 4 }
    /// <summary>
    /// 移動方法　線形補間 or 座標ベタ入れ
    /// </summary>
    public enum MoveType { Lerp, Warp }

    public static class Field
    {
        public struct Node
        {
            public int actualCost;          // 実コスト　 (移動コスト　距離や移動しにくさなど)
            public double estimatedCost;    // 推定コスト (目標までの距離)
            public double score;            // スコア     (実コスト + 推定コスト)
            public Status status;           // ノードの状態
        }

        /// <summary>
        /// 周囲8方向のPointを取得
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
        /// Vector3 から Vector2Int へ変換
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static Vector2Int ToPoint(this Vector3 pos) =>
            new Vector2Int { x = (int)Mathf.Floor(pos.x), y = (int)Mathf.Floor(pos.y) };
        public static Vector2Int ToPoint(int x, int y) => new Vector2Int(x, y);

        /// <summary>
        /// Vector2Int から Vector3 へ変換
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
        private Vector2Int _startPosition, _targetPosition;              // 開始地点・目標地点
        private List<Vector2Int> _root = new List<Vector2Int>();         // 最短経路
        private List<Vector2Int> _branchPoint = new List<Vector2Int>();  // 分岐地点
        private double _hashScore = 0;                                   // スコア(一時保存)
        private int _attemptsCount = 0;                                  // 試行回数
        private Status _targetStatus = Status.Target;                    // 目標地点のステータス

        public int line = 8;                      // Y-axis  行
        public int column = 15;                   // X-axis  列
        [SerializeField] GameObject _targetObj;             // 目標となるオブジェクト
        [SerializeField] GameObject _wall;                  // 壁を子オブジェクトに持つ箱
        [SerializeField] Button _startButton;               // 開始ボタン　(ボタンコンポーネントは変更しなくてよい)
        [SerializeField] int _maxAttempts = 100;            // 最大試行回数
        [SerializeField] float _searchDelayTime = .25f;     // 経路探索中の遅延時間
        [SerializeField] float _traceDelayTime = .15f;      // 最短経路探索中の遅延時間
        [Space(20)]
        [SerializeField] MoveDirection _moveDirection;      // 移動方向
        [SerializeField] MoveType _moveType;                // 移動方法

        private void Awake() => _startButton.onClick.AddListener(Init);

        private void Init()
        {
            // ノード初期化
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

            // 開始地点取得
            _startPosition = transform.position.ToPoint();
            _node[_startPosition] = new Field.Node()
            {
                actualCost = (int)Cost.Ground,
                status = Status.Start
            };

            // 目標地点取得
            _targetPosition = _targetObj.transform.position.ToPoint();
            _node[_targetPosition] = new Field.Node()
            {
                actualCost = (int)Cost.Ground,
                status = Status.Target
            };

            // 壁設定
            for (int i = 0; i < _wall.transform.childCount; i++)
            {
                var pos = _wall.transform.GetChild(i).position.ToPoint();
                _node[pos] = new Field.Node() { status = Status.Wall };
            }

            Open();
        }

        /// <summary>
        /// 周囲のスコアを求める
        /// </summary>
        private void Open()
        {
            // 試行回数が最大試行回数を超えた場合
            if (++_attemptsCount > _maxAttempts)
            {
                Debug.LogError("Overflow: 試行回数が最大試行回数を超えました");
                UnityEditor.EditorApplication.isPaused = true;
                return;
            }

                // debug
                //print(_attemptsCount + "回目");
            var around = new Dictionary<Vector2Int, double>();

            // 周囲の最少スコアを求める
            for (var i = 0; i < (int)_moveDirection; i++)
            {
                Vector2Int pos = transform.position.ToPoint().MovePosition(i);
                if (pos.x < 0 || pos.y < 0 || pos.x > column || pos.y > line) continue;
                else if (_node[pos].status == Status.Wall) continue;
                else if (_node[pos].status == Status.Closed) continue;
                else if (_node[pos].status == (_targetStatus == Status.Start ? Status.Target : Status.Start)) continue;

                // ターゲットを見つけたら計算終了
                if (_node[pos].status == _targetStatus)
                {
                    _hashScore = 0;
                    Move(pos, _targetStatus);
                    return;
                }

                // 推定コスト　計算
                var estimated = Field.ToDistance(_targetPosition, pos);

                var hashStatus = _node[pos].status;
                // スコア　計算
                _node[pos] = new Field.Node()
                {
                    status = hashStatus,
                    actualCost = (int)Cost.Ground + _attemptsCount,
                    estimatedCost = estimated,
                    score = (int)Cost.Ground + _attemptsCount + estimated
                };

                // スコアがない(Openしていない)場合は考慮しない
                if (_node[pos].score <= 0)
                {
                    continue;
                }
                around.Add(pos,  _node[pos].score);
            }

            // 分岐地点を保持しておく
            if (around.Count > 1)
            {
                _branchPoint.Add(transform.position.ToPoint());
            }
            // もし移動可能地点がなければ分岐地点に戻る
            else if (around.Count == 0)
            {
                if(_branchPoint.Count == 0)
                {
                    Debug.LogError("Overflow: 通路を発見することができませんでした");
                    UnityEditor.EditorApplication.isPaused = true;
                    return;
                }
                transform.position = _branchPoint[_branchPoint.Count - 1].ToPosition();
                _branchPoint.Remove(_branchPoint[_branchPoint.Count - 1]);
                Open();
                return;
            }

            // 最小コストの座標
            var minCost = around.OrderBy(p => p.Value).First();
            _hashScore = minCost.Value;

            Move(minCost.Key, _targetStatus);
        }

        /// <summary>
        /// 移動
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
        /// 通ってきた道を引き返さないために閉じる
        /// </summary>
        /// <param name="beforePoint"></param>
        private void Closed(Vector2Int beforePoint)
        {
            var hashStatus = beforePoint==_startPosition ? Status.Start : Status.Closed;
            var hashActual = _node[beforePoint].actualCost;
            _node[beforePoint] = new Field.Node() {  actualCost = hashActual, score = _hashScore, status = hashStatus };
        }

        /// <summary>
        /// ゴールから逆算して最短ルートを求める
        /// </summary>
        /// <param name="val"></param>
        private void GetMinCostCoordinate()
        {
            var isBreak = false; ;
            // 最短経路の検索
            _root.Add(transform.position.ToPoint());

            // Targetの地点を登録
            var minCost = new KeyValuePair<Vector2Int, double>();

            int n = 0;

            // Startまでの最短経路を検索
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
                    // 実コストが小さいものを探す
                    closed.Add(pos, _node[pos].actualCost);
                }

                if (isBreak) break;
                // 最小実コストの座標
                minCost = closed.OrderBy(p => p.Value).ThenBy(p => Field.ToDistance(p.Key, _startPosition)).First();

                _root.Add(minCost.Key);
            } while (n++ < _maxAttempts);

            _root.Add(_startPosition);
            transform.position = _startPosition.ToPosition();
            // 最短経路の検索　終了

            // "ターゲット->スタート" から "スタート->ターゲット" に直す
            _root.Reverse();

            TraceRoot(_root);
        }

        private void TraceRoot(List<Vector2Int> root)
        {
            StartCoroutine(Trace(root));
        }

        /// <summary>
        /// 最短経路を移動
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
