﻿// zlib/libpng License
//
// Copyright (c) 2018 Sinoa
//
// This software is provided 'as-is', without any express or implied warranty.
// In no event will the authors be held liable for any damages arising from the use of this software.
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it freely,
// subject to the following restrictions:
//
// 1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software.
//    If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IceMilkTea.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace IceMilkTea.Service
{
    #region サービス本体
    /// <summary>
    /// ゲーム自身が利用するゲームアセットの管理方法の状態を確認する機能を提供するサービスクラスです
    /// </summary>
    public class AssetManifestService : GameService
    {
        // 定数定義
        private const int DefaultCapacity = 1 << 10;

        // メンバ変数定義
        private ManifestStorageHandler storageHandler;
        private List<AssetManifestFetcher> fetcherList;
        private Dictionary<ulong, AssetEntry> assetEntryTable;



        /// <summary>
        /// AssetManifestService のインスタンスを既定値を用いて初期化します
        /// </summary>
        public AssetManifestService() : this(new DefaultJsonManifestStorageHandler())
        {
        }


        /// <summary>
        /// AssetManifestService のインスタンスを初期化します
        /// </summary>
        /// <param name="storageHandler">AssetManifestService の情報をコントロールするハンドラ</param>
        /// <exception cref="ArgumentNullException">storageHandler が null です</exception>
        public AssetManifestService(ManifestStorageHandler storageHandler)
        {
            // nullを渡されたら
            if (storageHandler == null)
            {
                // 情報の保存ができない
                throw new ArgumentNullException(nameof(storageHandler));
            }


            // もろもろ初期化
            fetcherList = new List<AssetManifestFetcher>();
            assetEntryTable = new Dictionary<ulong, AssetEntry>(DefaultCapacity);
            this.storageHandler = storageHandler;
        }


        /// <summary>
        /// AssetManifestFetcher を登録します
        /// </summary>
        /// <param name="fetcher">登録する AssetManifestFetcher</param>
        /// <exception cref="ArgumentNullException">fetcher が null です</exception>
        /// <exception cref="InvalidOperationException">既に登録済みの fetcher です</exception>
        public void RegisterAssetManifestFetcher(AssetManifestFetcher fetcher)
        {
            // null を渡されたら
            if (fetcher == null)
            {
                // それは受け付けられない
                throw new ArgumentNullException(nameof(fetcher));
            }


            // 既に同じインスタンスが存在していたら
            if (fetcherList.Contains(fetcher))
            {
                // 同じインスタンスの登録は許されない
                throw new InvalidOperationException($"既に登録済みの {nameof(fetcher)} です");
            }


            // 追加する
            fetcherList.Add(fetcher);
        }


        /// <summary>
        /// マニフェストのフェッチを非同期で行います
        /// </summary>
        /// <param name="progress">フェッチの進捗通知を受ける IProgress 不要の場合は null の指定が可能です</param>
        /// <returns>マニフェストのフェッチを非同期で操作しているタスクを返します</returns>
        public async Task FetchManifestAsync(IProgress<double> progress)
        {
            // 管理しているマニフェストの数で1を割って1フェッチャーあたりの進捗率を求める
            var unitProgressRate = 1.0 / fetcherList.Count;


            // 進捗オフセット及び進捗率を扱う進捗通知受付オブジェクトを生成する
            var progressOffset = 0.0;
            var fetcherProgress = new Progress<double>(x => progress?.Report(x * unitProgressRate + progressOffset));


            // フェッチャーの数分ループ
            for (int i = 0; i < fetcherList.Count; ++i)
            {
                // 進捗オフセットを求めて、フェッチャーの非同期実行を行う
                progressOffset = i * unitProgressRate;
                var manifest = await fetcherList[i].FetchAssetManifestAsync(fetcherProgress);


                // アセットエントリが存在しないなら
                if (manifest.AssetEntries == null)
                {
                    // 何事もなかったかのように次へ
                    continue;
                }


                // アセットエントリの数分回る
                for (int j = 0; j < manifest.AssetEntries.Length; ++j)
                {
                    // アセットエントリの名前からIDを作ってエントリテーブルに設定する
                    var assetEntryId = manifest.AssetEntries[j].Name.ToCrc64Code();
                    assetEntryTable[assetEntryId] = manifest.AssetEntries[j];
                }
            }
        }


        /// <summary>
        /// 指定されたアセット名から、フェッチ済みアセットエントリを取得します。
        /// </summary>
        /// <param name="assetName">取得するアセットエントリのアセット名</param>
        /// <param name="entry">取得されたアセットエントリを格納します</param>
        /// <exception cref="KeyNotFoundException">'{assetName}'のアセットエントリが見つかりませんでした</exception>
        public void GetAssetEntry(string assetName, out AssetEntry entry)
        {
            // アセット名からIDを作って値の取得を試みるが、失敗したら
            if (!assetEntryTable.TryGetValue(assetName.ToCrc64Code(), out entry))
            {
                // 取得出来なかったことを例外で吐く
                throw new KeyNotFoundException($"'{assetName}'のアセットエントリが見つかりませんでした");
            }
        }


        /// <summary>
        /// フェッチ済みアセットエントリの列挙可能オブジェクトを取得します
        /// </summary>
        /// <returns>アセットエントリの列挙可能なオブジェクトを返します</returns>
        public IEnumerable<AssetEntry> GetAssetEntryEnumerable()
        {
            // アセット管理テーブルの値列挙を返す
            return assetEntryTable.Values;
        }
    }
    #endregion



    #region マニフェスト関連構造体
    /// <summary>
    /// AssetManifestService が扱うマニフェストのルート構造を持った構造体です
    /// </summary>
    [Serializable]
    public struct AssetManifestRoot
    {
        /// <summary>
        /// マニフェスト名
        /// </summary>
        public string Name;

        /// <summary>
        /// マニフェストバージョン
        /// </summary>
        public int Version;

        /// <summary>
        /// マニフェストを生成したUNIXタイムスタンプ（ミリ秒）
        /// </summary>
        public long CreatedTimeStamp;

        /// <summary>
        /// マニフェストに登録されている
        /// </summary>
        public AssetEntry[] AssetEntries;
    }



    /// <summary>
    /// マニフェストに含まれるアセットの情報を持った構造体です
    /// </summary>
    [Serializable]
    public struct AssetEntry
    {
        /// <summary>
        /// アセット名
        /// </summary>
        public string Name;

        /// <summary>
        /// アセットURL
        /// </summary>
        public string AssetUrl;

        /// <summary>
        /// フェッチURL
        /// </summary>
        public string FetchUrl;

        /// <summary>
        /// インストールURL
        /// </summary>
        public string InstallUrl;

        /// <summary>
        /// アセットサイズ
        /// </summary>
        public long Size;

        /// <summary>
        /// 分割されたファイルの分割された総数。
        /// </summary>
        public int DivideTotalCount;

        /// <summary>
        /// 分割されたファイルの分割インデックス番号
        /// </summary>
        public int PartIndex;

        /// <summary>
        /// アセットハッシュ
        /// </summary>
        public byte[] AssetHash;
    }



    /// <summary>
    /// AssetManifestService によるデータをシリアライズするための構造体です
    /// </summary>
    [Serializable]
    public struct AssetManifestServiceData
    {
        /// <summary>
        /// 管理しているアセットエントリの配列
        /// </summary>
        AssetEntry[] HoldingAssetEntries;
    }
    #endregion



    #region Abstract ManifestFetcher＆ManifestStorageHandler
    /// <summary>
    /// AssetManifestRoot をフェッチするフェッチャー抽象クラスです
    /// </summary>
    public abstract class AssetManifestFetcher
    {
        /// <summary>
        /// アセットマニフェストを非同期でフェッチします
        /// </summary>
        /// <param name="progress">マニフェストのフェッチの進捗通知を受ける IProgress</param>
        /// <returns>マニフェストを非同期でフェッチするタスクを返します</returns>
        public abstract Task<AssetManifestRoot> FetchAssetManifestAsync(IProgress<double> progress);
    }



    /// <summary>
    /// AssetManifestService が保持するデータを操作するハンドラ抽象クラスです
    /// </summary>
    public abstract class ManifestStorageHandler
    {
        /// <summary>
        /// AssetManifestService の情報を保存する操作を非同期で行います
        /// </summary>
        /// <param name="data">保存する情報そのもの</param>
        /// <returns>保存操作のタスクを返します</returns>
        public abstract Task SaveAsync(AssetManifestServiceData data);


        /// <summary>
        /// AssetManifestService を読み込む操作を非同期で行います
        /// </summary>
        /// <returns>AssetManifestServiceData の非同期操作タスクを返します</returns>
        public abstract Task<AssetManifestServiceData> LoadAsync();


        /// <summary>
        /// 保存されているデータが存在しているかどうか確認します
        /// </summary>
        /// <returns>存在するなら true を、存在しないなら false を返します</returns>
        public abstract bool Exists();
    }
    #endregion



    #region Impl DefaultHttpAssetManifestFetcher
    /// <summary>
    /// HTTPを使った比較的単純なアセットマニフェストフェッチャークラスです
    /// </summary>
    public class DefaultHttpAssetManifestFetcher : AssetManifestFetcher
    {
        // メンバ変数定義
        private Serializer serializer;
        private Uri fetchTargetUrl;



        /// <summary>
        /// DefaultHttpAssetManifestFetcher のインスタンスを既定シリアライザで初期化します
        /// </summary>
        /// <param name="targetUrl">フェッチするターゲットURL</param>
        public DefaultHttpAssetManifestFetcher(string targetUrl) : this(targetUrl, new UnityJsonSerializer())
        {
        }


        /// <summary>
        /// DefaultHttpAssetManifestFetcher のインスタンスを初期化します
        /// </summary>
        /// <param name="targetUrl">フェッチするターゲットURL</param>
        /// <param name="serializer">フェッチしたデータをシリアライズするシリアライザ</param>
        /// <exception cref="ArgumentException">フェッチするターゲットURLが無効かnullです</exception>
        /// <exception cref="ArgumentNullException">serializer が null です</exception>
        public DefaultHttpAssetManifestFetcher(string targetUrl, Serializer serializer)
        {
            // もし targetUrl が無効なら
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                // どこから引っ張ってくれば良いですか
                throw new ArgumentException("フェッチするターゲットURLが無効かnullです", nameof(targetUrl));
            }


            // もしシリアライザがnullなら
            if (serializer == null)
            {
                // データが扱えない
                throw new ArgumentNullException(nameof(serializer));
            }


            // URIを生成してシリアライザを覚える
            fetchTargetUrl = new Uri(targetUrl);
            this.serializer = serializer;
        }


        /// <summary>
        /// マニフェストのフェッチを非同期で行います
        /// </summary>
        /// <param name="progress">フェッチの進捗通知を受け取る IProgress 通知を受けない場合は null 指定が可能です</param>
        /// <returns>フェッチの非同期操作をしているタスクを返します</returns>
        public override async Task<AssetManifestRoot> FetchAssetManifestAsync(IProgress<double> progress)
        {
            // まずはUnityWebRequestでデータを非同期ダウンロードして、進捗通知はそのまま伝える
            var request = UnityWebRequest.Get(fetchTargetUrl);
            request.downloadHandler = new DownloadHandlerManageBuffer();
            await request.SendWebRequest().ToAwaitable(new Progress<float>(downloadProgress => progress?.Report(downloadProgress)));


            // ダウンロードされたデータを非同期でデシリアライズして返す
            return await serializer.DeserializeAsync(request.downloadHandler.data);
        }



        /// <summary>
        /// UnityWebRequest によるC#のマネージバッファに対してダウンロードするダウンロードハンドラクラスです
        /// </summary>
        private sealed class DownloadHandlerManageBuffer : DownloadHandlerScript
        {
            // メンバ変数定義
            private byte[] buffer;
            private int contentLength;
            private int downloadedLength;



            /// <summary>
            /// DownloadHandlerManageBuffer を既定バッファを用いて初期化します
            /// </summary>
            public DownloadHandlerManageBuffer() : base(new byte[4 << 10])
            {
            }


            /// <summary>
            /// Content-Lengthの値を受信した時のハンドリングを行います
            /// </summary>
            /// <param name="contentLength">受信したコンテンツの長さ</param>
            protected override void ReceiveContentLength(int contentLength)
            {
                // コンテンツの長さを覚えてバッファの確保
                this.contentLength = contentLength;
                buffer = new byte[contentLength];
            }


            /// <summary>
            /// データの受信をした時のハンドリングを行います
            /// </summary>
            /// <param name="data">受信したデータのバッファ</param>
            /// <param name="dataLength">受信したデータの有効な長さ</param>
            /// <returns>受信を継続する場合は true を、中断する場合は false を返します</returns>
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                // 自身のバッファに受信データをコピーして、ダウンロード済みデータ長に受信長を加算する
                Array.Copy(data, 0, buffer, downloadedLength, dataLength);
                downloadedLength += dataLength;


                // ダウンロードは継続する
                return true;
            }


            /// <summary>
            /// 現在の進捗を取得します
            /// </summary>
            /// <returns>現在の進捗を返します</returns>
            protected override float GetProgress()
            {
                // ダウンロード済みの長さをコンテンツの長さで割った割合を返す
                return downloadedLength / (float)contentLength;
            }


            /// <summary>
            /// 受信済みのデータのバッファを取得します
            /// </summary>
            /// <returns></returns>
            protected override byte[] GetData()
            {
                // 現在保持しているバッファをそのまま返す（コピーはしない）
                return buffer;
            }
        }



        /// <summary>
        /// 受信したデータをシリアライズするシリアライザ抽象クラスです
        /// </summary>
        public abstract class Serializer
        {
            /// <summary>
            /// マニフェストからバッファにシリアライズを非同期で行います
            /// </summary>
            /// <param name="manifest">シリアライズするマニフェスト</param>
            /// <returns>シリアライズする非同期タスクを返します</returns>
            public abstract Task<byte[]> SerializeAsync(AssetManifestRoot manifest);


            /// <summary>
            /// バッファから AssetManifestRoot へデシリアライズを非同期で行います
            /// </summary>
            /// <param name="buffer">デシリアライズするべき受信データバッファ</param>
            /// <returns>AssetManifestRoot へデシリアライズする非同期タスクを返します</returns>
            public abstract Task<AssetManifestRoot> DeserializeAsync(byte[] buffer);
        }



        /// <summary>
        /// UnityのJsonUtilityを用いて AssetManifestRoot をシリアライズするシリアライザクラスです
        /// </summary>
        private sealed class UnityJsonSerializer : Serializer
        {
            /// <summary>
            /// バッファから AssetManifestRoot へデシリアライズを非同期で行います
            /// </summary>
            /// <param name="buffer">デシリアライズするべき受信データバッファ</param>
            /// <returns>AssetManifestRoot へデシリアライズする非同期タスクを返します</returns>
            public override Task<byte[]> SerializeAsync(AssetManifestRoot manifest)
            {
                // Jsonシリアライズするタスクを生成して返す
                return Task.Run(() =>
                {
                    // マニフェストをシリアライズしてUTF8エンコードして返す
                    var jsonData = JsonUtility.ToJson(manifest);
                    var encode = new UTF8Encoding(false);
                    return encode.GetBytes(jsonData);
                });
            }


            /// <summary>
            /// バッファから AssetManifestRoot へデシリアライズを非同期で行います
            /// </summary>
            /// <param name="buffer">デシリアライズするべき受信データバッファ</param>
            /// <returns>AssetManifestRoot へデシリアライズする非同期タスクを返します</returns>
            public override Task<AssetManifestRoot> DeserializeAsync(byte[] buffer)
            {
                // Jsonデシリアライズするタスクを生成して返す
                return Task.Run(() =>
                {
                    // UTF8デコード後デシリアライズして返す
                    var encode = new UTF8Encoding(false);
                    var jsonData = encode.GetString(buffer);
                    return JsonUtility.FromJson<AssetManifestRoot>(jsonData);
                });
            }
        }
    }
    #endregion



    #region Impl DefaultJsonManifestStorageHandler
    /// <summary>
    /// Jsonフォーマットに基づいたマニフェストストレージハンドラクラスです。
    /// また AssetManifestService の既定実装クラスになります。
    /// </summary>
    public class DefaultJsonManifestStorageHandler : ManifestStorageHandler
    {
        // メンバ変数定義
        private FileInfo configFileInfo;



        /// <summary>
        /// DefaultJsonManifestStorageHandler のインスタンスを既定パスを用いて初期化します
        /// </summary>
        public DefaultJsonManifestStorageHandler() : this(GetDefaultSaveFilePath())
        {
        }


        /// <summary>
        /// DefaultJsonManifestStorageHandler のインスタンスを初期化します
        /// </summary>
        /// <param name="saveFilePath">保存する先のファイルパス</param>
        /// <exception cref="ArgumentException">saveFilePath が 無効な値 または null です</exception>
        public DefaultJsonManifestStorageHandler(string saveFilePath)
        {
            // まともな文字列が渡されていない または 無効なパス文字が含まれていたら
            var invalidPath = saveFilePath?.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
            var invalidName = saveFilePath?.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
            if (string.IsNullOrWhiteSpace(saveFilePath) || invalidPath || invalidName)
            {
                // そういった文字列を受け付けない
                throw new ArgumentException($"{nameof(saveFilePath)} が 無効な値 または null です", nameof(saveFilePath));
            }


            // ファイル情報として覚えておく
            configFileInfo = new FileInfo(saveFilePath);
        }


        /// <summary>
        /// AssetManifestService の情報を保存する操作を非同期で行います
        /// </summary>
        /// <param name="data">保存する情報の参照</param>
        /// <returns>保存操作のタスクを返します</returns>
        public override Task SaveAsync(AssetManifestServiceData data)
        {
            // データを非同期で保存を行うタスクを実行する
            return Task.Run(async () =>
            {
                // データをJsonデータとしてシリアライズしてUTF8エンコードする
                var jsonData = JsonUtility.ToJson(data);
                var encode = new UTF8Encoding(false);
                var encodedJsonData = encode.GetBytes(jsonData);


                // 作業前に念の為ファイル情報を最新に更新する
                configFileInfo.Refresh();


                // まずはディレクトリが存在しないなら
                if (!configFileInfo.Directory.Exists)
                {
                    // 保存用ディレクトリの作成
                    configFileInfo.Directory.Create();
                }


                // ファイルを書き込みストリームで開く
                using (var stream = configFileInfo.OpenWrite())
                {
                    // 非同期で書き込む
                    await stream.WriteAsync(encodedJsonData, 0, encodedJsonData.Length);
                }


                // ファイル情報を最新に更新
                configFileInfo.Refresh();
            });
        }


        /// <summary>
        /// AssetManifestService を読み込む操作を非同期で行います
        /// </summary>
        /// <returns>AssetManifestServiceData の非同期操作タスクを返します</returns>
        /// <exception cref="AggregateException">非同期操作のタスクに例外が発生しました</exception>
        /// <exception cref="FileNotFoundException">AssetManifestServiceDataのファイルが見つかりませんでした（AggregateExceptionに内包されます）</exception>
        public override Task<AssetManifestServiceData> LoadAsync()
        {
            // データを非同期で読み込みを行うタスクを実行する
            return Task.Run(async () =>
            {
                // 作業する前に念の為ファイル情報を最新に更新する
                configFileInfo.Refresh();


                // もしファイルが存在しないなら
                if (!configFileInfo.Exists)
                {
                    // ロードは出来ない例外を吐く
                    throw new FileNotFoundException("AssetManifestServiceDataのファイルが見つかりませんでした", configFileInfo.FullName);
                }


                // ファイルを読み込みストリームで開く
                var jsonData = default(string);
                using (var stream = new StreamReader(configFileInfo.OpenRead(), new UTF8Encoding(false)))
                {
                    // 非同期でファイル全体の文字列をすべて読み込む
                    jsonData = await stream.ReadToEndAsync();
                }


                // Jsonデータからデシリアライズして返す
                return JsonUtility.FromJson<AssetManifestServiceData>(jsonData);
            });
        }


        /// <summary>
        /// 保存されているデータが存在しているかどうか確認します
        /// </summary>
        /// <returns>存在するなら true を、存在しないなら false を返します</returns>
        public override bool Exists()
        {
            // ファイル情報を更新してExistsの値をそのまま返す
            configFileInfo.Refresh();
            return configFileInfo.Exists;
        }


        /// <summary>
        /// マニフェストルートの既定保存ファイルパスを取得します
        /// </summary>
        /// <returns>既定ファイルパスを返します</returns>
        private static string GetDefaultSaveFilePath()
        {
            // Unityが提示する永続保存ディレクトリパスから生成する
            return Path.Combine(Application.persistentDataPath, "IceMilkTea", "AssetManifestService", "root.json");
        }
    }
    #endregion
}