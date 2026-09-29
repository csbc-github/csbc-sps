using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PlyProcessor.Core;

/// <summary>
/// 把三角網格寫成 CAD 可讀的 AP214 STEP（閉合殼實體）。
/// 每個三角形都會建立完整拓樸：
/// CARTESIAN_POINT → VERTEX_POINT → LINE → EDGE_CURVE → ORIENTED_EDGE → EDGE_LOOP → FACE_OUTER_BOUND → ADVANCED_FACE。
/// </summary>
public static class StepExporter
{
    /// <summary>
    /// 匯出網格。輸出前會檢查 Entity ID、參考、面、邊與閉合殼。
    /// </summary>
    /// <param name="mesh">已建立的三角網格。</param>
    /// <param name="name">實體與檔名用的名稱。</param>
    /// <returns>UTF-8 STEP 內容。</returns>
    /// <exception cref="InvalidOperationException">網格不合法，或寫出的 STEP 未通過檢查。</exception>
    public static byte[] Export(MeshBuilder.Mesh mesh, string name = "scan")
    {
        StepDocument document = new StepDocument();
        string stepText = document.Build(mesh, name);
        Validate(stepText);
        return Encoding.UTF8.GetBytes(stepText);
    }

    /// <summary>
    /// 檢查寫出的 STEP：ID 不重複、參考都有定義、必備實體存在，且檔尾正確。
    /// </summary>
    /// <param name="stepText">完整 STEP 文字。</param>
    private static void Validate(string stepText)
    {
        bool hasAdvancedFace = false;
        bool hasClosedShell = false;
        bool hasEdgeCurve = false;
        bool hasManifoldSolid = false;
        bool shellHasFace = false;
        HashSet<int> definedIds = new HashSet<int>();
        HashSet<int> referencedIds = new HashSet<int>();
        int dataStart = stepText.IndexOf("DATA;", StringComparison.Ordinal);
        int dataEnd = stepText.LastIndexOf("ENDSEC;", StringComparison.Ordinal);
        string ending = stepText.TrimEnd();

        if (dataStart < 0 || dataEnd < dataStart)
        {
            throw new InvalidOperationException("STEP 缺少 DATA 區段。");
        }

        if (!ending.EndsWith("ENDSEC;" + Environment.NewLine + "END-ISO-10303-21;", StringComparison.Ordinal)
            && !ending.EndsWith("ENDSEC;\nEND-ISO-10303-21;", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("STEP 結尾必須是 ENDSEC; 與 END-ISO-10303-21;。");
        }

        string dataBody = stepText.Substring(dataStart + "DATA;".Length, dataEnd - (dataStart + "DATA;".Length));
        string[] statements = dataBody.Split(';');
        for (int statementIndex = 0; statementIndex < statements.Length; statementIndex++)
        {
            string statement = statements[statementIndex].Trim();
            if (statement.Length == 0)
            {
                continue;
            }

            Match definition = Regex.Match(statement, @"^#(\d+)\s*=\s*(.+)$", RegexOptions.Singleline);
            if (!definition.Success)
            {
                throw new InvalidOperationException("STEP DATA 含有沒有 Entity ID 的敘述。");
            }

            int entityId = int.Parse(definition.Groups[1].Value, CultureInfo.InvariantCulture);
            string body = definition.Groups[2].Value.Trim();
            if (!definedIds.Add(entityId))
            {
                throw new InvalidOperationException($"STEP Entity ID 重複：#{entityId}。");
            }

            if (body.StartsWith("ADVANCED_FACE(", StringComparison.Ordinal))
            {
                hasAdvancedFace = true;
            }

            if (body.StartsWith("EDGE_CURVE(", StringComparison.Ordinal))
            {
                hasEdgeCurve = true;
            }

            if (body.StartsWith("MANIFOLD_SOLID_BREP(", StringComparison.Ordinal))
            {
                hasManifoldSolid = true;
            }

            if (body.StartsWith("CLOSED_SHELL(", StringComparison.Ordinal))
            {
                hasClosedShell = true;
                shellHasFace = Regex.IsMatch(body, @"CLOSED_SHELL\('(?:[^']|'')*',\(\s*#\d+", RegexOptions.Singleline);
            }

            foreach (Match reference in Regex.Matches(body, @"#(\d+)"))
            {
                referencedIds.Add(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        foreach (int referencedId in referencedIds)
        {
            if (!definedIds.Contains(referencedId))
            {
                throw new InvalidOperationException($"STEP 參考了不存在的 Entity：#{referencedId}。");
            }
        }

        if (!hasAdvancedFace)
        {
            throw new InvalidOperationException("STEP 缺少 ADVANCED_FACE。");
        }

        if (!hasEdgeCurve)
        {
            throw new InvalidOperationException("STEP 缺少 EDGE_CURVE。");
        }

        if (!hasClosedShell || !shellHasFace)
        {
            throw new InvalidOperationException("STEP 缺少非空的 CLOSED_SHELL。");
        }

        if (!hasManifoldSolid)
        {
            throw new InvalidOperationException("STEP 缺少 MANIFOLD_SOLID_BREP。");
        }
    }

    /// <summary>
    /// 組出與法線垂直的參考方向，供 AXIS2_PLACEMENT_3D 使用。
    /// </summary>
    private static void PerpendicularDirection(
        float normalX,
        float normalY,
        float normalZ,
        out float referenceX,
        out float referenceY,
        out float referenceZ)
    {
        float absoluteX = MathF.Abs(normalX);
        float absoluteY = MathF.Abs(normalY);
        float absoluteZ = MathF.Abs(normalZ);
        float axisX = 0f;
        float axisY = 0f;
        float axisZ = 0f;
        float length;

        if (absoluteX <= absoluteY && absoluteX <= absoluteZ)
        {
            axisX = 1f;
        }
        else if (absoluteY <= absoluteZ)
        {
            axisY = 1f;
        }
        else
        {
            axisZ = 1f;
        }

        referenceX = normalY * axisZ - normalZ * axisY;
        referenceY = normalZ * axisX - normalX * axisZ;
        referenceZ = normalX * axisY - normalY * axisX;
        length = MathF.Sqrt(referenceX * referenceX + referenceY * referenceY + referenceZ * referenceZ);
        if (length < 1e-8f)
        {
            throw new InvalidOperationException("無法由面法線算出不平行的參考方向。");
        }

        referenceX /= length;
        referenceY /= length;
        referenceZ /= length;
    }

    /// <summary>
    /// STEP 字串：空白名稱改為 scan，單引號依規格重複。
    /// </summary>
    private static string ToStepString(string value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "scan" : value.Trim();
        text = text.Replace("\\", "/", StringComparison.Ordinal);
        text = text.Replace("\r", " ", StringComparison.Ordinal);
        text = text.Replace("\n", " ", StringComparison.Ordinal);
        text = text.Replace("'", "''", StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// STEP 實數。接近零的值寫成 0，避免 -0.000000。
    /// </summary>
    private static string FormatNumber(float value)
    {
        if (MathF.Abs(value) < 0.0000005f)
        {
            value = 0f;
        }

        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 單一 ID 產生器與實體緩衝。私有巢狀型別只服務 StepExporter。
    /// </summary>
    private sealed class StepDocument
    {
        private readonly List<string> _entities = new List<string>();
        private readonly StringBuilder _text = new StringBuilder();
        private int _nextId;

        /// <summary>
        /// 寫出標頭、產品、單位、幾何拓樸與結尾。
        /// </summary>
        public string Build(MeshBuilder.Mesh mesh, string name)
        {
            string safeName = ToStepString(name);
            string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            ValidateMesh(mesh);

            _text.AppendLine("ISO-10303-21;");
            _text.AppendLine("HEADER;");
            _text.AppendLine("FILE_DESCRIPTION(('PLY Processor STEP export'),'2;1');");
            _text.Append("FILE_NAME('").Append(safeName).Append(".stp','").Append(timestamp).AppendLine("',(''),(''),'','','');");
            _text.AppendLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));");
            _text.AppendLine("ENDSEC;");
            _text.AppendLine("DATA;");

            int advancedBrepId;
            int applicationContextId = Add("APPLICATION_CONTEXT('core data for automotive mechanical design processes')");
            int definitionContextId;
            int definitionId;
            int definitionShapeId;
            int formationId;
            int geometricContextId;
            int lengthUnitId = Add("( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )");
            int planeAngleUnitId = Add("( NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.) )");
            int productContextId;
            int productId;
            int protocolId = Add($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{applicationContextId})");
            int shapeRepresentationId;
            int shellId;
            int solidAngleUnitId = Add("( NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT() )");
            int solidId;
            int uncertaintyId = Add($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-07),#{lengthUnitId},'distance_accuracy_value','confusion accuracy')");
            int[] pointIds = WritePoints(mesh.Vertices);
            int[] vertexIds = WriteVertices(pointIds);
            List<int> faceIds;

            geometricContextId = Add("( GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#" + uncertaintyId.ToString(CultureInfo.InvariantCulture) + ")) GLOBAL_UNIT_ASSIGNED_CONTEXT((#" + lengthUnitId.ToString(CultureInfo.InvariantCulture) + ",#" + planeAngleUnitId.ToString(CultureInfo.InvariantCulture) + ",#" + solidAngleUnitId.ToString(CultureInfo.InvariantCulture) + ")) REPRESENTATION_CONTEXT('Context','3D') )");
            productContextId = Add($"PRODUCT_CONTEXT('',#{applicationContextId},'mechanical')");
            productId = Add($"PRODUCT('{safeName}','{safeName}','',(#{productContextId}))");
            formationId = Add($"PRODUCT_DEFINITION_FORMATION('','',#{productId})");
            definitionContextId = Add($"PRODUCT_DEFINITION_CONTEXT('part definition',#{applicationContextId},'design')");
            definitionId = Add($"PRODUCT_DEFINITION('design','',#{formationId},#{definitionContextId})");
            definitionShapeId = Add($"PRODUCT_DEFINITION_SHAPE('','',#{definitionId})");
            faceIds = WriteFaces(mesh, pointIds, vertexIds);
            shellId = AddClosedShell(faceIds);
            solidId = Add($"MANIFOLD_SOLID_BREP('{safeName}',#{shellId})");
            advancedBrepId = Add($"ADVANCED_BREP_SHAPE_REPRESENTATION('',(#{solidId}),#{geometricContextId})");
            shapeRepresentationId = Add($"SHAPE_DEFINITION_REPRESENTATION(#{definitionShapeId},#{advancedBrepId})");

            if (protocolId <= 0 || shapeRepresentationId <= 0)
            {
                throw new InvalidOperationException("STEP 產品結構的 Entity ID 不合法。");
            }

            for (int entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
            {
                _text.AppendLine(_entities[entityIndex]);
            }

            _text.AppendLine("ENDSEC;");
            _text.AppendLine("END-ISO-10303-21;");
            return _text.ToString();
        }

        /// <summary>
        /// 配置下一個未使用的 Entity ID。全檔只走這一個產生器。
        /// </summary>
        private int NextId()
        {
            _nextId++;
            return _nextId;
        }

        /// <summary>
        /// 追加一筆實體。body 不含開頭的 #id=。
        /// </summary>
        private int Add(string body)
        {
            int id = NextId();
            _entities.Add("#" + id.ToString(CultureInfo.InvariantCulture) + "=" + body + ";");
            return id;
        }

        /// <summary>
        /// 每個頂點一筆 CARTESIAN_POINT。
        /// </summary>
        private int[] WritePoints(List<float[]> vertices)
        {
            int[] pointIds = new int[vertices.Count];
            for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
            {
                float[] coordinates = vertices[vertexIndex];
                pointIds[vertexIndex] = Add(
                    "CARTESIAN_POINT('',("
                    + FormatNumber(coordinates[0]) + ","
                    + FormatNumber(coordinates[1]) + ","
                    + FormatNumber(coordinates[2]) + "))");
            }

            return pointIds;
        }

        /// <summary>
        /// 每個點一筆 VERTEX_POINT，引用對應的 CARTESIAN_POINT。
        /// </summary>
        private int[] WriteVertices(int[] pointIds)
        {
            int[] vertexIds = new int[pointIds.Length];
            for (int vertexIndex = 0; vertexIndex < pointIds.Length; vertexIndex++)
            {
                vertexIds[vertexIndex] = Add("VERTEX_POINT('',#" + pointIds[vertexIndex].ToString(CultureInfo.InvariantCulture) + ")");
            }

            return vertexIds;
        }

        /// <summary>
        /// 依三角形繞向寫面。共用邊只建一次 EDGE_CURVE，方向由較小頂點索引指向較大索引。
        /// </summary>
        private List<int> WriteFaces(MeshBuilder.Mesh mesh, int[] pointIds, int[] vertexIds)
        {
            Dictionary<(int Low, int High), int> edgeCurveIds = new Dictionary<(int Low, int High), int>();
            List<int> faceIds = new List<int>(mesh.Triangles.Count);
            for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
            {
                float edgeU0;
                float edgeU1;
                float edgeU2;
                float edgeV0;
                float edgeV1;
                float edgeV2;
                float normalLength;
                float normalX;
                float normalY;
                float normalZ;
                float referenceX;
                float referenceY;
                float referenceZ;
                int boundId;
                int faceId;
                int first = mesh.Triangles[triangleIndex][0];
                int loopId;
                int normalDirectionId;
                int orientedAB;
                int orientedBC;
                int orientedCA;
                int placementId;
                int planeId;
                int referenceDirectionId;
                int second = mesh.Triangles[triangleIndex][1];
                int third = mesh.Triangles[triangleIndex][2];
                float[] pointA = mesh.Vertices[first];
                float[] pointB = mesh.Vertices[second];
                float[] pointC = mesh.Vertices[third];

                edgeU0 = pointB[0] - pointA[0];
                edgeU1 = pointB[1] - pointA[1];
                edgeU2 = pointB[2] - pointA[2];
                edgeV0 = pointC[0] - pointA[0];
                edgeV1 = pointC[1] - pointA[1];
                edgeV2 = pointC[2] - pointA[2];
                normalX = edgeU1 * edgeV2 - edgeU2 * edgeV1;
                normalY = edgeU2 * edgeV0 - edgeU0 * edgeV2;
                normalZ = edgeU0 * edgeV1 - edgeU1 * edgeV0;
                normalLength = MathF.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
                if (normalLength < 1e-8f)
                {
                    throw new InvalidOperationException($"三角形 {triangleIndex} 面積為零，無法建立 ADVANCED_FACE。");
                }

                normalX /= normalLength;
                normalY /= normalLength;
                normalZ /= normalLength;
                PerpendicularDirection(normalX, normalY, normalZ, out referenceX, out referenceY, out referenceZ);

                orientedAB = WriteOrientedEdge(first, second, edgeCurveIds, pointIds, vertexIds, mesh.Vertices);
                orientedBC = WriteOrientedEdge(second, third, edgeCurveIds, pointIds, vertexIds, mesh.Vertices);
                orientedCA = WriteOrientedEdge(third, first, edgeCurveIds, pointIds, vertexIds, mesh.Vertices);
                loopId = Add(
                    "EDGE_LOOP('',(#"
                    + orientedAB.ToString(CultureInfo.InvariantCulture) + ",#"
                    + orientedBC.ToString(CultureInfo.InvariantCulture) + ",#"
                    + orientedCA.ToString(CultureInfo.InvariantCulture) + "))");
                boundId = Add("FACE_OUTER_BOUND('',#" + loopId.ToString(CultureInfo.InvariantCulture) + ",.T.)");
                normalDirectionId = Add("DIRECTION('',(" + FormatNumber(normalX) + "," + FormatNumber(normalY) + "," + FormatNumber(normalZ) + "))");
                referenceDirectionId = Add("DIRECTION('',(" + FormatNumber(referenceX) + "," + FormatNumber(referenceY) + "," + FormatNumber(referenceZ) + "))");
                placementId = Add(
                    "AXIS2_PLACEMENT_3D('',#"
                    + pointIds[first].ToString(CultureInfo.InvariantCulture) + ",#"
                    + normalDirectionId.ToString(CultureInfo.InvariantCulture) + ",#"
                    + referenceDirectionId.ToString(CultureInfo.InvariantCulture) + ")");
                planeId = Add("PLANE('',#" + placementId.ToString(CultureInfo.InvariantCulture) + ")");
                faceId = Add(
                    "ADVANCED_FACE('',(#"
                    + boundId.ToString(CultureInfo.InvariantCulture) + "),#"
                    + planeId.ToString(CultureInfo.InvariantCulture) + ",.T.)");
                faceIds.Add(faceId);
            }

            return faceIds;
        }

        /// <summary>
        /// 依三角形這一側的走向，決定 ORIENTED_EDGE 是 .T. 還是 .F.。
        /// EDGE_CURVE 的原始方向固定為較小頂點索引到較大頂點索引。
        /// </summary>
        private int WriteOrientedEdge(
            int fromVertex,
            int toVertex,
            Dictionary<(int Low, int High), int> edgeCurveIds,
            int[] pointIds,
            int[] vertexIds,
            List<float[]> vertices)
        {
            bool sameAsCurve;
            int curveId;
            int highIndex = Math.Max(fromVertex, toVertex);
            int lowIndex = Math.Min(fromVertex, toVertex);
            int orientedId;

            if (lowIndex == highIndex)
            {
                throw new InvalidOperationException("三角形含有重複頂點，無法建立 EDGE_CURVE。");
            }

            sameAsCurve = fromVertex == lowIndex;
            if (!edgeCurveIds.TryGetValue((lowIndex, highIndex), out curveId))
            {
                curveId = WriteEdgeCurve(lowIndex, highIndex, pointIds, vertexIds, vertices);
                edgeCurveIds.Add((lowIndex, highIndex), curveId);
            }

            orientedId = Add(
                "ORIENTED_EDGE('',*,*,#"
                + curveId.ToString(CultureInfo.InvariantCulture)
                + ",."
                + (sameAsCurve ? "T" : "F")
                + ".)");
            return orientedId;
        }

        /// <summary>
        /// 建立 LINE 與 EDGE_CURVE。曲線由 lowIndex 指向 highIndex，same_sense 為 .T.。
        /// </summary>
        private int WriteEdgeCurve(
            int lowIndex,
            int highIndex,
            int[] pointIds,
            int[] vertexIds,
            List<float[]> vertices)
        {
            float deltaX = vertices[highIndex][0] - vertices[lowIndex][0];
            float deltaY = vertices[highIndex][1] - vertices[lowIndex][1];
            float deltaZ = vertices[highIndex][2] - vertices[lowIndex][2];
            float length = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
            int curveId;
            int directionId;
            int lineId;
            int vectorId;

            if (length < 1e-8f)
            {
                throw new InvalidOperationException("邊長為零，無法建立 EDGE_CURVE。");
            }

            deltaX /= length;
            deltaY /= length;
            deltaZ /= length;
            directionId = Add("DIRECTION('',(" + FormatNumber(deltaX) + "," + FormatNumber(deltaY) + "," + FormatNumber(deltaZ) + "))");
            vectorId = Add("VECTOR('',#" + directionId.ToString(CultureInfo.InvariantCulture) + ",1.)");
            lineId = Add("LINE('',#" + pointIds[lowIndex].ToString(CultureInfo.InvariantCulture) + ",#" + vectorId.ToString(CultureInfo.InvariantCulture) + ")");
            curveId = Add(
                "EDGE_CURVE('',#"
                + vertexIds[lowIndex].ToString(CultureInfo.InvariantCulture) + ",#"
                + vertexIds[highIndex].ToString(CultureInfo.InvariantCulture) + ",#"
                + lineId.ToString(CultureInfo.InvariantCulture)
                + ",.T.)");
            return curveId;
        }

        /// <summary>
        /// 把所有 ADVANCED_FACE 收成一個非空 CLOSED_SHELL。面清單過長時換行，敘述仍以分號結束。
        /// </summary>
        private int AddClosedShell(List<int> faceIds)
        {
            StringBuilder body = new StringBuilder();
            int id = NextId();
            if (faceIds.Count == 0)
            {
                throw new InvalidOperationException("沒有面，無法建立 CLOSED_SHELL。");
            }

            body.Append("CLOSED_SHELL('',(");
            for (int faceIndex = 0; faceIndex < faceIds.Count; faceIndex++)
            {
                if (faceIndex > 0)
                {
                    body.Append(',');
                }

                if (faceIndex > 0 && faceIndex % 12 == 0)
                {
                    body.Append("\n");
                }

                body.Append('#');
                body.Append(faceIds[faceIndex].ToString(CultureInfo.InvariantCulture));
            }

            body.Append("))");
            _entities.Add("#" + id.ToString(CultureInfo.InvariantCulture) + "=" + body.ToString() + ";");
            return id;
        }

        /// <summary>
        /// 匯出前確認網格有頂點、三角形，且索引落在範圍內。
        /// </summary>
        private static void ValidateMesh(MeshBuilder.Mesh mesh)
        {
            if (mesh is null)
            {
                throw new InvalidOperationException("網格是 null，無法匯出 STEP。");
            }

            if (mesh.Vertices is null || mesh.Vertices.Count == 0)
            {
                throw new InvalidOperationException("網格沒有頂點，無法匯出 STEP。");
            }

            if (mesh.Triangles is null || mesh.Triangles.Count == 0)
            {
                throw new InvalidOperationException("網格沒有三角形，無法匯出 STEP。");
            }

            for (int vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                float[] coordinates = mesh.Vertices[vertexIndex];
                if (coordinates is null || coordinates.Length < 3)
                {
                    throw new InvalidOperationException($"頂點 {vertexIndex} 缺少座標。");
                }
            }

            for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
            {
                int[] triangle = mesh.Triangles[triangleIndex];
                if (triangle is null || triangle.Length != 3)
                {
                    throw new InvalidOperationException($"三角形 {triangleIndex} 不是三個頂點。");
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    if (triangle[corner] < 0 || triangle[corner] >= mesh.Vertices.Count)
                    {
                        throw new InvalidOperationException($"三角形 {triangleIndex} 的頂點索引超出範圍。");
                    }
                }
            }
        }
    }
}
