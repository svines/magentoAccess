﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MagentoAccess.Misc;
using MagentoAccess.Models.CreateOrders;
using MagentoAccess.Models.CreateProducts;
using MagentoAccess.Models.Credentials;
using MagentoAccess.Models.DeleteProducts;
using MagentoAccess.Models.GetMagentoCoreInfo;
using MagentoAccess.Models.GetOrders;
using MagentoAccess.Models.GetProducts;
using MagentoAccess.Models.PingRest;
using MagentoAccess.Models.PutInventory;
using MagentoAccess.Models.Services.Rest.GetStockItems;
using MagentoAccess.Models.Services.Soap.GetCategoryTree;
using MagentoAccess.Models.Services.Soap.GetProductAttributeInfo;
using MagentoAccess.Models.Services.Soap.GetProductAttributeMediaList;
using MagentoAccess.Models.Services.Soap.GetProductInfo;
using MagentoAccess.Models.Services.Soap.GetStockItems;
using MagentoAccess.Models.Services.Soap.PutStockItems;
using MagentoAccess.Services.Rest;
using MagentoAccess.Services.Soap;
using MagentoAccess.Services.Soap._1_14_1_0_ee;
using MagentoAccess.Services.Soap._1_7_0_1_ce_1_9_0_1_ce;
using MagentoAccess.Services.Soap._1_9_2_1_ce;
using Netco.Extensions;

namespace MagentoAccess
{
	public class MagentoService : IMagentoService
	{
		public bool UseSoapOnly { get; set; }
		internal virtual IMagentoServiceLowLevelRest MagentoServiceLowLevelRest { get; set; }
		internal virtual IMagentoServiceLowLevelSoap MagentoServiceLowLevelSoap { get; set; }
		internal MagentoServiceLowLevelSoapFactory MagentoServiceLowLevelSoapFactory { get; set; }

		public delegate void SaveAccessToken( string token, string secret );

		public SaveAccessToken AfterGettingToken { get; set; }
		public TransmitVerificationCodeDelegate TransmitVerificationCode { get; set; }
		public Func< string > AdditionalLogInfo { get; set; }

		public async Task< IEnumerable< CreateProductModelResult > > CreateProductAsync( IEnumerable< CreateProductModel > models )
		{
			var methodParameters = models.ToJson();
			var mark = Mark.CreateNew();

			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( methodParameters, mark ) );

				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				//crunch for old versions
				var magentoServiceLowLevelSoap = MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );

				var productsCreationInfo = await models.ProcessInBatchAsync( 30, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "CreatingProduct: {0}", CreateMethodCallInfo( mark : mark, methodParameters : x.ToJson() ) ) );

					var res = new CreateProductModelResult( x );
					await ActionPolicies.GetAsync.Get( async () => res.Result = await magentoServiceLowLevelSoap.CreateProduct( x.StoreId, x.Name, x.Sku, x.IsInStock ).ConfigureAwait( false ) ).ConfigureAwait( false );

					MagentoLogger.LogTrace( string.Format( "ProductCreated: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : x.ToJson() ) ) );
					return res;
				} ).ConfigureAwait( false );

				var productsCreationInfoString = productsCreationInfo.ToJson();

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters, notes : "ProductsCerated:\"{0}\"".FormatWith( productsCreationInfoString ) ) );

				return productsCreationInfo;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< CreateOrderModelResult > > CreateOrderAsync( IEnumerable< CreateOrderModel > models )
		{
			var methodParameters = models.ToJson();
			var mark = Mark.CreateNew();

			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( methodParameters, mark ) );

				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				//crunch for old versions
				var magentoServiceLowLevelSoap = MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );

				var productsCreationInfo = await models.ProcessInBatchAsync( 30, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "CreatingOrder: {0}", CreateMethodCallInfo( mark : mark, methodParameters : x.ToJson() ) ) );

					var res = new CreateOrderModelResult( x );

					var shoppingCartIdTask = magentoServiceLowLevelSoap.CreateCart( x.StoreId );
					shoppingCartIdTask.Wait();
					var _shoppingCartId = shoppingCartIdTask.Result;

					var shoppingCartCustomerSetTask = magentoServiceLowLevelSoap.ShoppingCartGuestCustomerSet( _shoppingCartId, x.CustomerFirstName, x.CustomerMail, x.CustomerLastName, x.StoreId );
					shoppingCartCustomerSetTask.Wait();

					var shoppingCartAddressSet = magentoServiceLowLevelSoap.ShoppingCartAddressSet( _shoppingCartId, x.StoreId );
					shoppingCartAddressSet.Wait();

					var productTask = magentoServiceLowLevelSoap.ShoppingCartAddProduct( _shoppingCartId, x.ProductIds.First(), x.StoreId );
					productTask.Wait();

					var shippingMenthodTask = magentoServiceLowLevelSoap.ShoppingCartSetShippingMethod( _shoppingCartId, x.StoreId );
					shippingMenthodTask.Wait();

					var paymentMenthodTask = magentoServiceLowLevelSoap.ShoppingCartSetPaymentMethod( _shoppingCartId, x.StoreId );
					paymentMenthodTask.Wait();

					var orderIdTask = magentoServiceLowLevelSoap.CreateOrder( _shoppingCartId, x.StoreId );
					orderIdTask.Wait();
					res.OrderId = orderIdTask.Result;
					Task.Delay( 1000 );

					MagentoLogger.LogTrace( string.Format( "OrderCreated: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : x.ToJson() ) ) );
					return res;
				} ).ConfigureAwait( false );

				var productsCreationInfoString = productsCreationInfo.ToJson();

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters, notes : "OrdersCerated:\"{0}\"".FormatWith( productsCreationInfoString ) ) );

				return productsCreationInfo;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< DeleteProductModelResult > > DeleteProductAsync( IEnumerable< DeleteProductModel > models )
		{
			var methodParameters = models.ToJson();
			var mark = Mark.CreateNew();

			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( methodParameters, mark ) );

				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				//crunch for old versions
				var magentoServiceLowLevelSoap = MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );

				var productsCreationInfo = await models.ProcessInBatchAsync( 30, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "DeleteProduct: {0}", CreateMethodCallInfo( mark : mark, methodParameters : x.ToJson() ) ) );

					var res = new DeleteProductModelResult( x );
					res.Result = await magentoServiceLowLevelSoap.DeleteProduct( x.StoreId, x.CategoryId, x.ProductId, x.IdentiferType ).ConfigureAwait( false );

					MagentoLogger.LogTrace( string.Format( "ProductDeleted: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : x.ToJson() ) ) );
					return res;
				} ).ConfigureAwait( false );

				var productsCreationInfoString = productsCreationInfo.ToJson();

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters, notes : "ProductsDeleted:\"{0}\"".FormatWith( productsCreationInfoString ) ) );

				return productsCreationInfo;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		#region constructor
		public MagentoService( MagentoAuthenticatedUserCredentials magentoAuthenticatedUserCredentials )
		{
			this.MagentoServiceLowLevelRest = new MagentoServiceLowLevelRestRest(
				magentoAuthenticatedUserCredentials.ConsumerKey,
				magentoAuthenticatedUserCredentials.ConsumerSckretKey,
				magentoAuthenticatedUserCredentials.BaseMagentoUrl,
				magentoAuthenticatedUserCredentials.AccessToken,
				magentoAuthenticatedUserCredentials.AccessTokenSecret
				);

			this.MagentoServiceLowLevelSoap = new MagentoServiceLowLevelSoap_v_from_1_7_to_1_9_CE(
				magentoAuthenticatedUserCredentials.SoapApiUser,
				magentoAuthenticatedUserCredentials.SoapApiKey,
				magentoAuthenticatedUserCredentials.BaseMagentoUrl,
				null
				);

			var MagentoServiceLowLevelSoap_1_14_1_EE = new MagentoServiceLowLevelSoap_v_1_14_1_0_EE(
				magentoAuthenticatedUserCredentials.SoapApiUser,
				magentoAuthenticatedUserCredentials.SoapApiKey,
				magentoAuthenticatedUserCredentials.BaseMagentoUrl,
				null
				);

			var MagentoServiceLowLevelSoap_1_9_2_1_CE = new MagentoServiceLowLevelSoap_v_1_9_2_1_ce(
				magentoAuthenticatedUserCredentials.SoapApiUser,
				magentoAuthenticatedUserCredentials.SoapApiKey,
				magentoAuthenticatedUserCredentials.BaseMagentoUrl,
				null
				);

			//all methods should use factory, but it takes time to convert them, since there are a lot of errors in magento which we should avoid
			MagentoServiceLowLevelSoapFactory = new MagentoServiceLowLevelSoapFactory( null,
				magentoAuthenticatedUserCredentials.BaseMagentoUrl,
				magentoAuthenticatedUserCredentials.SoapApiKey,
				magentoAuthenticatedUserCredentials.SoapApiUser,
				new Dictionary< string, IMagentoServiceLowLevelSoap >
				{
					{ MagentoVersions.M_1_9_2_0, MagentoServiceLowLevelSoap_1_9_2_1_CE },
					{ MagentoVersions.M_1_9_2_1, MagentoServiceLowLevelSoap_1_9_2_1_CE },
					{ MagentoVersions.M_1_9_2_2, MagentoServiceLowLevelSoap_1_9_2_1_CE },
					{ MagentoVersions.M_1_9_0_1, this.MagentoServiceLowLevelSoap },
					{ MagentoVersions.M_1_8_1_0, this.MagentoServiceLowLevelSoap },
					{ MagentoVersions.M_1_7_0_2, this.MagentoServiceLowLevelSoap },
					{ MagentoVersions.M_1_14_1_0, MagentoServiceLowLevelSoap_1_14_1_EE },
				}
				);
		}

		public MagentoService( MagentoNonAuthenticatedUserCredentials magentoUserCredentials )
		{
			this.MagentoServiceLowLevelRest = new MagentoServiceLowLevelRestRest(
				magentoUserCredentials.ConsumerKey,
				magentoUserCredentials.ConsumerSckretKey,
				magentoUserCredentials.BaseMagentoUrl,
				magentoUserCredentials.RequestTokenUrl,
				magentoUserCredentials.AuthorizeUrl,
				magentoUserCredentials.AccessTokenUrl
				);
		}
		#endregion

		#region ping
		public async Task< PingSoapInfo > PingSoapAsync( Mark mark = null )
		{
			if( mark.IsBlank() )
				mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );
				var magentoInfo = await this.MagentoServiceLowLevelSoap.GetMagentoInfoAsync().ConfigureAwait( false );
				var soapWorks = !string.IsNullOrWhiteSpace( magentoInfo.MagentoVersion ) || !string.IsNullOrWhiteSpace( magentoInfo.MagentoEdition );

				var magentoCoreInfo = new PingSoapInfo( magentoInfo.MagentoVersion, magentoInfo.MagentoEdition, soapWorks );
				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : magentoCoreInfo.ToJson() ) );

				return magentoCoreInfo;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< PingRestInfo > PingRestAsync()
		{
			var mark = Mark.CreateNew();
			const string currentMenthodName = "PingRestAsync";
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );

				var magentoOrders = await this.MagentoServiceLowLevelRest.GetProductsAsync( 1, 1, true ).ConfigureAwait( false );
				var restWorks = magentoOrders.Products != null;
				var magentoCoreInfo = new PingRestInfo( restWorks );

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : magentoCoreInfo.ToJson() ) );

				return magentoCoreInfo;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}
		#endregion

		#region getOrders
		public async Task< IEnumerable< Order > > GetOrdersAsync( IEnumerable< string > orderIds )
		{
			var methodParameters = orderIds.ToJson();

			var mark = Mark.CreateNew();

			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( methodParameters, mark ) );

				IMagentoServiceLowLevelSoap magentoServiceLowLevelSoap;
				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				//crunch for old versions
				magentoServiceLowLevelSoap = String.Equals( pingres.Edition, MagentoVersions.M_1_7_0_2, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_8_1_0, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_9_0_1, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_14_1_0, StringComparison.CurrentCultureIgnoreCase ) ? this.MagentoServiceLowLevelSoap : MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );

				var salesOrderInfoResponses = await orderIds.ProcessInBatchAsync( 16, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "OrderRequested: {0}", CreateMethodCallInfo( mark : mark, methodParameters : x ) ) );
					var res = await magentoServiceLowLevelSoap.GetOrderAsync( x ).ConfigureAwait( false );
					MagentoLogger.LogTrace( string.Format( "OrderReceived: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : x ) ) );
					return res;
				} ).ConfigureAwait( false );

				var salesOrderInfoResponsesList = salesOrderInfoResponses.ToList();

				var resultOrders = new List< Order >();

				const int batchSize = 500;
				for( var i = 0; i < salesOrderInfoResponsesList.Count; i += batchSize )
				{
					var orderInfoResponses = salesOrderInfoResponsesList.Skip( i ).Take( batchSize );
					var resultOrderPart = orderInfoResponses.AsParallel().Select( x => new Order( x ) ).ToList();
					resultOrders.AddRange( resultOrderPart );
					var resultOrdersBriefInfo = resultOrderPart.ToJsonAsParallel( 0, batchSize );
					var partDescription = "From: " + i.ToString() + "," + ( ( i + batchSize < salesOrderInfoResponsesList.Count ) ? batchSize : salesOrderInfoResponsesList.Count % batchSize ).ToString() + " items(or few)";
					MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : resultOrdersBriefInfo, methodParameters : methodParameters, notes : "LogPart:\"{0}\"".FormatWith( partDescription ) ) );
				}

				return resultOrders;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< Order > > GetOrdersAsync( DateTime dateFrom, DateTime dateTo )
		{
			var dateFromUtc = TimeZoneInfo.ConvertTimeToUtc( dateFrom );
			var dateToUtc = TimeZoneInfo.ConvertTimeToUtc( dateTo );
			var methodParameters = string.Format( "{{dateFrom:{0},dateTo:{1}}}", dateFromUtc, dateToUtc );

			var mark = Mark.CreateNew();

			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( methodParameters, mark ) );

				var interval = new TimeSpan( 7, 0, 0, 0 );
				var intervalOverlapping = new TimeSpan( 0, 0, 0, 1 );

				var dates = SplitToDates( dateFromUtc, dateToUtc, interval, intervalOverlapping );

				IMagentoServiceLowLevelSoap magentoServiceLowLevelSoap;
				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				//crunch for old versions
				magentoServiceLowLevelSoap = String.Equals( pingres.Edition, MagentoVersions.M_1_7_0_2, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_8_1_0, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_9_0_1, StringComparison.CurrentCultureIgnoreCase )
				                             || String.Equals( pingres.Edition, MagentoVersions.M_1_14_1_0, StringComparison.CurrentCultureIgnoreCase ) ? this.MagentoServiceLowLevelSoap : MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );
				var ordersBriefInfos = await dates.ProcessInBatchAsync( 30, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "OrdersRequested: {0}", CreateMethodCallInfo( mark : mark, methodParameters : String.Format( "{0},{1}", x.Item1, x.Item2 ) ) ) );

					var res = await magentoServiceLowLevelSoap.GetOrdersAsync( x.Item1, x.Item2 ).ConfigureAwait( false );

					MagentoLogger.LogTrace( string.Format( "OrdersReceived: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : String.Format( "{0},{1}", x.Item1, x.Item2 ) ) ) );
					return res;
				} ).ConfigureAwait( false );

				var ordersBriefInfo = ordersBriefInfos.Where( x => x != null && x.Orders != null ).SelectMany( x => x.Orders ).ToList();

				ordersBriefInfo = ordersBriefInfo.Distinct( new SalesOrderByOrderIdComparer() ).ToList();

				var ordersBriefInfoString = ordersBriefInfo.ToJson();

				MagentoLogger.LogTrace( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters, notes : "BriefOrdersReceived:\"{0}\"".FormatWith( ordersBriefInfoString ) ) );

				var salesOrderInfoResponses = await ordersBriefInfo.ProcessInBatchAsync( 16, async x =>
				{
					MagentoLogger.LogTrace( string.Format( "OrderRequested: {0}", CreateMethodCallInfo( mark : mark, methodParameters : x.incrementId ) ) );
					var res = await magentoServiceLowLevelSoap.GetOrderAsync( x.incrementId ).ConfigureAwait( false );
					MagentoLogger.LogTrace( string.Format( "OrderReceived: {0}", CreateMethodCallInfo( mark : mark, methodResult : res.ToJson(), methodParameters : x.incrementId ) ) );
					return res;
				} ).ConfigureAwait( false );

				var salesOrderInfoResponsesList = salesOrderInfoResponses.ToList();

				var resultOrders = new List< Order >();

				const int batchSize = 500;
				for( var i = 0; i < salesOrderInfoResponsesList.Count; i += batchSize )
				{
					var orderInfoResponses = salesOrderInfoResponsesList.Skip( i ).Take( batchSize );
					var resultOrderPart = orderInfoResponses.AsParallel().Select( x => new Order( x ) ).ToList();
					resultOrders.AddRange( resultOrderPart );
					var resultOrdersBriefInfo = resultOrderPart.ToJsonAsParallel( 0, batchSize );
					var partDescription = "From: " + i.ToString() + "," + ( ( i + batchSize < salesOrderInfoResponsesList.Count ) ? batchSize : salesOrderInfoResponsesList.Count % batchSize ).ToString() + " items(or few)";
					MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : resultOrdersBriefInfo, methodParameters : methodParameters, notes : "LogPart:\"{0}\"".FormatWith( partDescription ) ) );
				}

				return resultOrders;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark, methodParameters : methodParameters ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< Order > > GetOrdersAsync()
		{
			var mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );
				var res = await this.MagentoServiceLowLevelRest.GetOrdersAsync().ConfigureAwait( false );
				var resHandled = res.Orders.Select( x => new Order( x ) );
				var orderBriefInfo = resHandled.ToJson();
				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : orderBriefInfo ) );
				return resHandled;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}
		#endregion

		#region getProducts
		public async Task< IEnumerable< Product > > GetProductsSimpleAsync()
		{
			var mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );
				var res = await this.GetRestProductsAsync().ConfigureAwait( false );

				var productBriefInfo = res.ToJson();
				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : productBriefInfo ) );

				return res;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< Product > > GetProductsAsync( bool includeDetails = false )
		{
			var mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );

				IEnumerable< Product > resultProducts;

				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				var magentoServiceLowLevel = MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );
				resultProducts = await this.GetProductsBySoap( magentoServiceLowLevel, includeDetails ).ConfigureAwait( false );

				var resultProductsBriefInfo = resultProducts.ToJson();

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : resultProductsBriefInfo ) );

				return resultProducts;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task< IEnumerable< Product > > FillProductsDetailsAsync( IEnumerable< Product > products )
		{
			var mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark ) );

				var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
				var magentoServiceLowLevel = MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );
				var resultProducts = await FillProductDetails( magentoServiceLowLevel, products ).ConfigureAwait( false );

				var resultProductsBriefInfo = resultProducts.ToJson();

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodResult : resultProductsBriefInfo ) );

				return resultProducts;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}
		#endregion

		#region updateInventory
		public async Task UpdateInventoryAsync( IEnumerable< Inventory > products )
		{
			var productsBriefInfo = products.ToJson();
			var mark = Mark.CreateNew();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark, methodParameters : productsBriefInfo ) );

				var inventories = products as IList< Inventory > ?? products.ToList();
				var updateBriefInfo = PredefinedValues.NotAvailable;
				if( inventories.Any() )
				{
					var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
					//crunch for 1702
					updateBriefInfo = String.Equals( pingres.Version, MagentoVersions.M_1_7_0_2, StringComparison.CurrentCultureIgnoreCase ) ? await this.UpdateStockItemsBySoapByThePiece( inventories, mark ).ConfigureAwait( false ) : await this.UpdateStockItemsBySoap( inventories, MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true ), mark ).ConfigureAwait( false );
				}

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodParameters : productsBriefInfo, methodResult : updateBriefInfo ) );
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public async Task UpdateInventoryBySkuAsync( IEnumerable< InventoryBySku > inventory )
		{
			var mark = Mark.CreateNew();
			var productsBriefInfo = inventory.ToJson();
			try
			{
				MagentoLogger.LogTraceStarted( CreateMethodCallInfo( mark : mark, methodParameters : productsBriefInfo ) );

				var inventories = inventory as IList< InventoryBySku > ?? inventory.ToList();
				var updateBriefInfo = PredefinedValues.NotAvailable;
				if( inventories.Any() )
				{
					if( this.UseSoapOnly )
					{
						var pingres = await this.PingSoapAsync( mark ).ConfigureAwait( false );
						var magentoServiceLowLevelSoap = String.Equals( pingres.Edition, MagentoVersions.M_1_7_0_2, StringComparison.CurrentCultureIgnoreCase )
						                                 || String.Equals( pingres.Edition, MagentoVersions.M_1_8_1_0, StringComparison.CurrentCultureIgnoreCase )
						                                 || String.Equals( pingres.Edition, MagentoVersions.M_1_9_0_1, StringComparison.CurrentCultureIgnoreCase )
						                                 || String.Equals( pingres.Edition, MagentoVersions.M_1_14_1_0, StringComparison.CurrentCultureIgnoreCase ) ? this.MagentoServiceLowLevelSoap : MagentoServiceLowLevelSoapFactory.GetMagentoServiceLowLevelSoap( pingres.Version, true );

						var stockitems = await magentoServiceLowLevelSoap.GetStockItemsAsync( inventory.Select( x => x.Sku ).ToList() ).ConfigureAwait( false );
						var productsWithSkuQtyId = from i in inventory join s in stockitems.InventoryStockItems on i.Sku equals s.Sku select new Inventory() { ItemId = s.ProductId, ProductId = s.ProductId, Qty = i.Qty };
						await this.UpdateInventoryAsync( productsWithSkuQtyId ).ConfigureAwait( false );
					}
					else
					{
						var productsWithSkuUpdatedQtyId = await this.GetProductsAsync().ConfigureAwait( false );
						var resultProducts = productsWithSkuUpdatedQtyId.Select( x => new Inventory() { ItemId = x.EntityId, ProductId = x.ProductId, Qty = x.Qty.ToLongOrDefault() } );
						await this.UpdateInventoryAsync( resultProducts ).ConfigureAwait( false );
					}
				}

				MagentoLogger.LogTraceEnded( CreateMethodCallInfo( mark : mark, methodParameters : productsBriefInfo, methodResult : updateBriefInfo ) );
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( CreateMethodCallInfo( mark : mark ), exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}
		#endregion

		#region auth
		public void InitiateDesktopAuthentication()
		{
			try
			{
				MagentoLogger.LogTraceStarted( string.Format( "InitiateDesktopAuthentication()" ) );
				this.MagentoServiceLowLevelRest.TransmitVerificationCode = this.TransmitVerificationCode;
				var authorizeTask = this.MagentoServiceLowLevelRest.InitiateDescktopAuthenticationProcess();
				authorizeTask.Wait();

				if( this.AfterGettingToken != null )
					this.AfterGettingToken.Invoke( this.MagentoServiceLowLevelRest.AccessToken, this.MagentoServiceLowLevelRest.AccessTokenSecret );

				MagentoLogger.LogTraceEnded( string.Format( "InitiateDesktopAuthentication()" ) );
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( "Error.", exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public VerificationData RequestVerificationUri()
		{
			try
			{
				MagentoLogger.LogTraceStarted( string.Format( "RequestVerificationUri()" ) );
				var res = this.MagentoServiceLowLevelRest.RequestVerificationUri();
				MagentoLogger.LogTraceEnded( string.Format( "RequestVerificationUri()" ) );

				return res;
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( "Error.", exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}

		public void PopulateAccessTokenAndAccessTokenSecret( string verificationCode, string requestToken, string requestTokenSecret )
		{
			try
			{
				MagentoLogger.LogTraceStarted( string.Format( "PopulateAccessTokenAndAccessTokenSecret(...)" ) );
				this.MagentoServiceLowLevelRest.PopulateAccessTokenAndAccessTokenSecret( verificationCode, requestToken, requestTokenSecret );

				if( this.AfterGettingToken != null )
					this.AfterGettingToken.Invoke( this.MagentoServiceLowLevelRest.AccessToken, this.MagentoServiceLowLevelRest.AccessTokenSecret );

				MagentoLogger.LogTraceEnded( string.Format( "PopulateAccessTokenAndAccessTokenSecret(...)" ) );
			}
			catch( Exception exception )
			{
				var mexc = new MagentoCommonException( "Error.", exception );
				MagentoLogger.LogTraceException( mexc );
				throw mexc;
			}
		}
		#endregion

		#region MethodsImplementations
		private string CreateMethodCallInfo( string methodParameters = "", Mark mark = null, string errors = "", string methodResult = "", string additionalInfo = "", [ CallerMemberName ] string memberName = "", string notes = "" )
		{
			additionalInfo = ( string.IsNullOrWhiteSpace( additionalInfo ) && this.AdditionalLogInfo != null ) ? AdditionalLogInfo() : PredefinedValues.EmptyJsonObject;
			mark = mark ?? Mark.Blank();
			var connectionInfo = this.MagentoServiceLowLevelSoap.ToJson();
			var str = string.Format(
				"{{MethodName:{0}, ConnectionInfo:{1}, MethodParameters:{2}, Mark:\"{3}\"{4}{5}{6}{7}}}",
				memberName,
				connectionInfo,
				methodParameters,
				mark,
				string.IsNullOrWhiteSpace( errors ) ? string.Empty : ", Errors:" + errors,
				string.IsNullOrWhiteSpace( methodResult ) ? string.Empty : ", Result:" + methodResult,
				string.IsNullOrWhiteSpace( notes ) ? string.Empty : ", Notes:" + notes,
				string.IsNullOrWhiteSpace( additionalInfo ) ? string.Empty : ", " + additionalInfo
				);
			return str;
		}

		private static List< Tuple< DateTime, DateTime > > SplitToDates( DateTime dateFromUtc, DateTime dateToUtc, TimeSpan interval, TimeSpan intervalOverlapping )
		{
			var dates = new List< Tuple< DateTime, DateTime > >();
			if( dateFromUtc > dateToUtc )
				return dates;
			var dateFromUtcCopy = dateFromUtc;
			var dateToUtcCopy = dateToUtc;
			while( dateFromUtcCopy < dateToUtcCopy )
			{
				dates.Add( Tuple.Create( dateFromUtcCopy, dateFromUtcCopy.Add( interval ).Add( intervalOverlapping ) ) );
				dateFromUtcCopy = dateFromUtcCopy.Add( interval );
			}
			var lastInterval = dates.Last();
			dates.Remove( lastInterval );
			dates.Add( Tuple.Create( lastInterval.Item1, dateToUtc ) );
			return dates;
		}

		private async Task< IEnumerable< Product > > GetProductsBySoap( IMagentoServiceLowLevelSoap magentoServiceLowLevelSoap, bool includeDetails )
		{
			const int stockItemsListMaxChunkSize = 1000;
			IEnumerable< Product > resultProducts = new List< Product >();
			var catalogProductListResponse = await magentoServiceLowLevelSoap.GetProductsAsync().ConfigureAwait( false );

			if( catalogProductListResponse == null || catalogProductListResponse.Products == null )
				return resultProducts;

			var products = catalogProductListResponse.Products.ToList();

			var productsDevidedByChunks = products.Batch( stockItemsListMaxChunkSize );

			// this code works to solw on 1 core server (but seems faster on multicore)
			//var getStockItemsAsyncTasks = productsDevidedByChunks.Select( stockItemsChunk => this.MagentoServiceLowLevelSoap.GetStockItemsAsync( stockItemsChunk.Select( x => x.sku ).ToList() ) );
			//var stockItemsResponses = await Task.WhenAll(getStockItemsAsyncTasks).ConfigureAwait(false);
			//if (stockItemsResponses == null || !stockItemsResponses.Any())
			//	return Enumerable.Empty<Product>();
			//var stockItems = stockItemsResponses.Where(x => x != null && x.result != null).SelectMany(x => x.result).ToList();

			// this code works faster on 1 core machine 
			var getStockItemsAsync = new List< InventoryStockItem >();
			foreach( var productsDevidedByChunk in productsDevidedByChunks )
			{
				var catalogInventoryStockItemListResponse = await magentoServiceLowLevelSoap.GetStockItemsAsync( productsDevidedByChunk.Select( x => x.Sku ).ToList() ).ConfigureAwait( false );
				getStockItemsAsync.AddRange( catalogInventoryStockItemListResponse.InventoryStockItems.ToList() );
			}
			var stockItems = getStockItemsAsync.ToList();

			resultProducts = ( from stockItemEntity in stockItems join productEntity in products on stockItemEntity.ProductId equals productEntity.ProductId select new Product( stockItemEntity.ProductId, productEntity.ProductId, productEntity.Name, productEntity.Sku, stockItemEntity.Qty, 0, null ) ).ToList();

			if( includeDetails )
				resultProducts = await FillProductDetails( magentoServiceLowLevelSoap, resultProducts );
			return resultProducts;
		}

		private static async Task< IEnumerable< Product > > FillProductDetails( IMagentoServiceLowLevelSoap magentoServiceLowLevelSoap, IEnumerable< Product > resultProducts )
		{
			var productAttributes = magentoServiceLowLevelSoap.GetManufacturersInfoAsync( ProductAttributeCodes.Manufacturer );
			var resultProductslist = resultProducts as IList< Product > ?? resultProducts.ToList();
			var attributes = new string[] { ProductAttributeCodes.Cost, ProductAttributeCodes.Manufacturer, ProductAttributeCodes.Upc };
			var productsInfoTask = resultProductslist.ProcessInBatchAsync( 10, async x => await magentoServiceLowLevelSoap.GetProductInfoAsync( x.ProductId, attributes, true ).ConfigureAwait( false ) );
			var mediaListResponsesTask = resultProductslist.ProcessInBatchAsync( 10, async x => await magentoServiceLowLevelSoap.GetProductAttributeMediaListAsync( x.ProductId ).ConfigureAwait( false ) );
			var categoriesTreeResponseTask = magentoServiceLowLevelSoap.GetCategoriesTreeAsync();
			await Task.WhenAll( productAttributes, productsInfoTask, mediaListResponsesTask, categoriesTreeResponseTask ).ConfigureAwait( false );
			var productsInfo = productsInfoTask.Result;
			var mediaListResponses = mediaListResponsesTask.Result;
			var magentoCategoriesList = categoriesTreeResponseTask.Result.RootCategory == null ? new List< CategoryNode >() : categoriesTreeResponseTask.Result.RootCategory.Flatten();

			Func< IEnumerable< Product >, IEnumerable< ProductAttributeMediaListResponse >, IEnumerable< Product > > FillImageUrls = ( prods, mediaLists ) =>
				( from rp in prods
					join pi in mediaLists on rp.ProductId equals pi.ProductId into pairs
					from pair in pairs.DefaultIfEmpty()
					select pair == null ? rp : new Product( rp, pair.MagentoImages.Select( x => new MagentoUrl( x ) ) ) );

			Func< IEnumerable< Product >, IEnumerable< CatalogProductInfoResponse >, IEnumerable< Product > > FillWeightDescriptionShortDescriptionPricev =
				( prods, prodInfos ) => ( from rp in prods
					join pi in prodInfos on rp.ProductId equals pi.ProductId into pairs
					from pair in pairs.DefaultIfEmpty()
					select pair == null ? rp : new Product( rp, upc : pair.GetUpcAttributeValue(), manufacturer : pair.GetManufacturerAttributeValue(), cost : pair.GetCostAttributeValue().ToDecimalOrDefault(), weight : pair.Weight, shortDescription : pair.ShortDescription, description : pair.Description, specialPrice : pair.SpecialPrice, price : pair.Price, categories : pair.CategoryIds.Select( z => new Category( z ) ) ) );

			Func< IEnumerable< Product >, CatalogProductAttributeInfoResponse, IEnumerable< Product > > FillManufactures =
				( prods, prodInfos ) => ( from rp in prods
					join pi in prodInfos != null ? prodInfos.Attributes : new List< ProductAttributeInfo >() on rp.Manufacturer equals pi.Value into pairs
					from pair in pairs.DefaultIfEmpty()
					select pair == null ? rp : new Product( rp, manufacturer : pair.Label ) );

			Func< IEnumerable< Product >, IEnumerable< Category >, IEnumerable< Product > > FillProductsDeepestCategory =
				( prods, categories ) => ( from prod in prods
					let prodCategories = ( from category in ( prod.Categories ?? Enumerable.Empty< Category >() )
						join category2 in categories on category.Id equals category2.Id
						select category2 )
					select new Product( prod, categories : prodCategories ) );

			resultProducts = FillWeightDescriptionShortDescriptionPricev( resultProductslist, productsInfo ).ToList();
			resultProducts = FillImageUrls( resultProducts, mediaListResponses ).ToList();
			resultProducts = FillManufactures( resultProducts, productAttributes.Result ).ToList();
			resultProducts = FillProductsDeepestCategory( resultProducts, magentoCategoriesList.Select( y => new Category( y ) ).ToList() ).ToList();
			return resultProducts;
		}

		private async Task< IEnumerable< Product > > GetProductsByRest()
		{
			IEnumerable< Product > resultProducts;

			// this code doesn't work for magento 1.8.0.1 http://www.magentocommerce.com/bug-tracking/issue/index/id/130
			// this code works for magento 1.9.0.1
			var stockItemsAsync = this.GetRestStockItemsAsync();

			var productsAsync = this.GetRestProductsAsyncPparallel();

			await Task.WhenAll( stockItemsAsync, productsAsync ).ConfigureAwait( false );

			var stockItems = stockItemsAsync.Result.ToList();

			var products = productsAsync.Result.ToList();
			//#if DEBUG
			//			var temps = stockItems.Select( x => string.Format( "INSERT INTO [dbo].[StockItems] ([EntityId] ,[ProductId] ,[Qty]) VALUES ('{0}','{1}','{2}');", x.EntityId, x.ProductId, x.Qty ) );
			//			var stockItemsStr = string.Join( "\n", temps );
			//			var tempp = products.Select( x => string.Format( "INSERT INTO [dbo].[Products2]([EntityId] ,[ProductId] ,[Description] ,[Name] ,[Sku] ,[Price]) VALUES ('{0}','{1}','','','{4}','{5}');", x.EntityId, x.ProductId, x.Description, x.Name, x.Sku, x.Price ) );
			//			var productsStr = string.Join( "\n", tempp );
			//#endif

			resultProducts = ( from stockItem in stockItems join product in products on stockItem.ProductId equals product.EntityId select new Product( stockItem.ProductId, stockItem.EntityId, product.Name, product.Sku, stockItem.Qty, product.Price, product.Description ) ).ToList();
			return resultProducts;
		}

		private async Task< string > UpdateStockItemsBySoapByThePiece( IList< Inventory > inventories, Mark mark )
		{
			var productToUpdate = inventories.Select( x => new PutStockItem( x ) ).ToList();

			var batchResponses = await productToUpdate.ProcessInBatchAsync( 5, async x => new Tuple< bool, List< PutStockItem > >( await this.MagentoServiceLowLevelSoap.PutStockItemAsync( x, mark ).ConfigureAwait( false ), new List< PutStockItem > { x } ) );

			var updateBriefInfo = batchResponses.Where( x => x.Item1 ).SelectMany( y => y.Item2 ).ToJson();

			var notUpdatedProducts = batchResponses.Where( x => !x.Item1 ).SelectMany( y => y.Item2 );

			var notUpdatedBriefInfo = notUpdatedProducts.ToJson();

			if( notUpdatedProducts.Any() )
				throw new Exception( string.Format( "Not updated {0}", notUpdatedBriefInfo ) );

			return updateBriefInfo;
		}

		private async Task< IEnumerable< Product > > GetRestProductsAsync()
		{
			var page = 1;
			const int itemsPerPage = 100;

			var getProductsResponse = await this.MagentoServiceLowLevelRest.GetProductsAsync( page, itemsPerPage ).ConfigureAwait( false );

			var productsChunk = getProductsResponse.Products;
			if( productsChunk.Count() < itemsPerPage )
				return productsChunk.Select( x => new Product( null, x.EntityId, x.Name, x.Sku, null, x.Price, x.Description ) );

			var receivedProducts = new List< Models.Services.Rest.GetProducts.Product >();

			var lastReceiveProducts = productsChunk;

			bool isLastAndCurrentResponsesHaveTheSameProducts;

			do
			{
				receivedProducts.AddRange( productsChunk );

				var getProductsTask = this.MagentoServiceLowLevelRest.GetProductsAsync( ++page, itemsPerPage );
				getProductsTask.Wait();
				productsChunk = getProductsTask.Result.Products;

				//var repeatedItems = from l in lastReceiveProducts join c in productsChunk on l.EntityId equals c.EntityId select l;
				var repeatedItems = from c in productsChunk join l in lastReceiveProducts on c.EntityId equals l.EntityId select l;

				lastReceiveProducts = productsChunk;

				isLastAndCurrentResponsesHaveTheSameProducts = repeatedItems.Any();

				// try to get items that was added before last iteration
				if( isLastAndCurrentResponsesHaveTheSameProducts )
				{
					var notRrepeatedItems = productsChunk.Where( x => !repeatedItems.Exists( r => r.EntityId == x.EntityId ) );
					receivedProducts.AddRange( notRrepeatedItems );
				}
			} while( !isLastAndCurrentResponsesHaveTheSameProducts );

			return receivedProducts.Select( x => new Product( null, x.EntityId, x.Name, x.Sku, null, x.Price, x.Description ) );
		}

		private async Task< IEnumerable< Product > > GetRestProductsAsyncPparallel()
		{
			var page = 1;
			const int itemsPerPage = 100;

			var getProductsResponse = await this.MagentoServiceLowLevelRest.GetProductsAsync( page, itemsPerPage ).ConfigureAwait( false );

			var productsChunk = getProductsResponse.Products;
			if( productsChunk.Count() < itemsPerPage )
				return productsChunk.Select( x => new Product( null, x.EntityId, x.Name, x.Sku, null, x.Price, x.Description ) );

			var receivedProducts = new List< Models.Services.Rest.GetProducts.Product >();

			var lastReceiveProducts = productsChunk;

			receivedProducts.AddRange( productsChunk );

			var getProductsTasks = new List< Task< List< Models.Services.Rest.GetProducts.Product > > >();

			getProductsTasks.Add( Task.Factory.StartNew( () => this.GetRestProducts( lastReceiveProducts, itemsPerPage, ref page ) ) );
			getProductsTasks.Add( Task.Factory.StartNew( () => this.GetRestProducts( lastReceiveProducts, itemsPerPage, ref page ) ) );
			getProductsTasks.Add( Task.Factory.StartNew( () => this.GetRestProducts( lastReceiveProducts, itemsPerPage, ref page ) ) );
			getProductsTasks.Add( Task.Factory.StartNew( () => this.GetRestProducts( lastReceiveProducts, itemsPerPage, ref page ) ) );

			await Task.WhenAll( getProductsTasks ).ConfigureAwait( false );

			var results = getProductsTasks.SelectMany( x => x.Result ).ToList();
			receivedProducts.AddRange( results );
			receivedProducts = receivedProducts.Distinct( new ProductComparer() ).ToList();

			return receivedProducts.Select( x => new Product( null, x.EntityId, x.Name, x.Sku, null, x.Price, x.Description ) );
		}

		private List< Models.Services.Rest.GetProducts.Product > GetRestProducts( IEnumerable< Models.Services.Rest.GetProducts.Product > lastReceiveProducts, int itemsPerPage, ref int page )
		{
			var localIsLastAndCurrentResponsesHaveTheSameProducts = true;
			var localLastReceivedProducts = lastReceiveProducts;
			var localReceivedProducts = new List< Models.Services.Rest.GetProducts.Product >();
			do
			{
				Interlocked.Increment( ref page );

				var getProductsTask = this.MagentoServiceLowLevelRest.GetProductsAsync( page, itemsPerPage );
				getProductsTask.Wait();
				var localProductsChunk = getProductsTask.Result.Products;

				var repeatedItems = from c in localProductsChunk join l in localLastReceivedProducts on c.EntityId equals l.EntityId select l;

				localLastReceivedProducts = localProductsChunk;

				localIsLastAndCurrentResponsesHaveTheSameProducts = repeatedItems.Any();

				// try to get items that was added before last iteration
				if( localIsLastAndCurrentResponsesHaveTheSameProducts )
				{
					var notRrepeatedItems = localProductsChunk.Where( x => !repeatedItems.Exists( r => r.EntityId == x.EntityId ) );
					localReceivedProducts.AddRange( notRrepeatedItems );
				}
				else
					localReceivedProducts.AddRange( localProductsChunk );
			} while( !localIsLastAndCurrentResponsesHaveTheSameProducts );

			return localReceivedProducts;
		}

		private async Task< IEnumerable< Product > > GetRestStockItemsAsync()
		{
			var page = 1;
			const int itemsPerPage = 100;

			var getProductsResponse = await this.MagentoServiceLowLevelRest.GetStockItemsAsync( page, itemsPerPage ).ConfigureAwait( false );

			var productsChunk = getProductsResponse.Items;
			if( productsChunk.Count() < itemsPerPage )
				return productsChunk.Select( x => new Product( null, x.ItemId, null, null, null, 0, null ) );

			var receivedProducts = new List< StockItem >();

			var lastReceiveProducts = productsChunk;

			bool isLastAndCurrentResponsesHaveTheSameProducts;

			do
			{
				receivedProducts.AddRange( productsChunk );

				var getProductsTask = this.MagentoServiceLowLevelRest.GetStockItemsAsync( ++page, itemsPerPage );
				getProductsTask.Wait();

				productsChunk = getProductsTask.Result.Items;

				var repeatedItems = from c in productsChunk join l in lastReceiveProducts on new { ItemId = c.ItemId, BackOrders = c.BackOrders, Qty = c.Qty } equals new { ItemId = l.ItemId, BackOrders = l.BackOrders, Qty = l.Qty } select l;

				lastReceiveProducts = productsChunk;

				isLastAndCurrentResponsesHaveTheSameProducts = repeatedItems.Any();

				// try to get items that was added before last iteration
				if( isLastAndCurrentResponsesHaveTheSameProducts )
				{
					var notRrepeatedItems = productsChunk.Where( x => !repeatedItems.Exists( r => new { ItemId = r.ItemId, BackOrders = r.BackOrders, Qty = r.Qty } != new { ItemId = x.ItemId, BackOrders = x.BackOrders, Qty = x.Qty } ) );
					receivedProducts.AddRange( notRrepeatedItems );
				}
			} while( !isLastAndCurrentResponsesHaveTheSameProducts );

			return receivedProducts.Select( x => new Product( x.ProductId, x.ItemId, null, null, x.Qty, 0, "" ) );
		}

		private async Task< string > UpdateStockItemsByRest( IList< Inventory > inventories, string markForLog = "" )
		{
			string updateBriefInfo;
			const int productsUpdateMaxChunkSize = 50;
			var inventoryItems = inventories.Select( x => new Models.Services.Rest.PutStockItems.StockItem
			{
				ItemId = x.ItemId,
				MinQty = x.MinQty,
				ProductId = x.ProductId,
				Qty = x.Qty,
				StockId = x.StockId,
			} ).ToList();

			var productsDevidedToChunks = inventoryItems.SplitToChunks( productsUpdateMaxChunkSize );

			var batchResponses = await productsDevidedToChunks.ProcessInBatchAsync( 1, async x => await this.MagentoServiceLowLevelRest.PutStockItemsAsync( x, markForLog ).ConfigureAwait( false ) ).ConfigureAwait( false );

			var updateResult = batchResponses.Where( y => y.Items != null ).SelectMany( x => x.Items ).ToList();

			var secessefullyUpdated = updateResult.Where( x => x.Code == "200" );

			var unSecessefullyUpdated = updateResult.Where( x => x.Code != "200" );

			updateBriefInfo = updateResult.ToJson();

			if( unSecessefullyUpdated.Any() )
				throw new Exception( string.Format( "Not updated: {0}, Updated: {1}", unSecessefullyUpdated.ToJson(), secessefullyUpdated.ToJson() ) );

			return updateBriefInfo;
		}

		private async Task< string > UpdateStockItemsBySoap( IList< Inventory > inventories, IMagentoServiceLowLevelSoap magentoService, Mark markForLog = null )
		{
			const int productsUpdateMaxChunkSize = 50;
			var productToUpdate = inventories.Select( x => new PutStockItem( x ) ).ToList();

			var productsDevidedToChunks = productToUpdate.SplitToChunks( productsUpdateMaxChunkSize );

			var batchResponses = await productsDevidedToChunks.ProcessInBatchAsync( 1, async x => new Tuple< bool, List< PutStockItem > >( await magentoService.PutStockItemsAsync( x, markForLog ).ConfigureAwait( false ), x ) );

			var updateBriefInfo = batchResponses.Where( x => x.Item1 ).SelectMany( y => y.Item2 ).ToJson();

			var notUpdatedProducts = batchResponses.Where( x => !x.Item1 ).SelectMany( y => y.Item2 );

			var notUpdatedBriefInfo = notUpdatedProducts.ToJson();

			if( notUpdatedProducts.Any() )
				throw new Exception( string.Format( "Not updated {0}", notUpdatedBriefInfo ) );

			return updateBriefInfo;
		}
		#endregion
	}

	public class ProductAttributeCodes
	{
		public const string Upc = "upc";
		public const string Cost = "cost";
		public const string Manufacturer = "manufacturer";
	}

	internal class ProductComparer : IEqualityComparer< Models.Services.Rest.GetProducts.Product >
	{
		public bool Equals( Models.Services.Rest.GetProducts.Product x, Models.Services.Rest.GetProducts.Product y )
		{
			return x.EntityId == y.EntityId;
		}

		public int GetHashCode( Models.Services.Rest.GetProducts.Product obj )
		{
			return obj.EntityId.GetHashCode();
		}
	}

	internal class SalesOrderByOrderIdComparer : IEqualityComparer< Models.Services.Soap.GetOrders.Order >
	{
		public bool Equals( Models.Services.Soap.GetOrders.Order x, Models.Services.Soap.GetOrders.Order y )
		{
			return x.incrementId == y.incrementId && x.OrderId == y.OrderId;
		}

		public int GetHashCode( Models.Services.Soap.GetOrders.Order obj )
		{
			return obj.OrderId.GetHashCode() ^ obj.incrementId.GetHashCode();
		}
	}
}