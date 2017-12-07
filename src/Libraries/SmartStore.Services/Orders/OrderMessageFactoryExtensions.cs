﻿using System;
using SmartStore.Core.Domain.Messages;
using SmartStore.Core.Domain.Orders;
using SmartStore.Core.Domain.Shipping;
using SmartStore.Services.Messages;

namespace SmartStore.Services.Orders
{
	public static class OrderMessageFactoryExtensions
	{
		/// <summary>
		/// Sends an order placed notification to a store owner
		/// </summary>
		public static CreateMessageResult SendOrderPlacedStoreOwnerNotification(this IMessageFactory factory, Order order, int languageId = 0)
		{
			Guard.NotNull(order, nameof(order));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.OrderPlacedStoreOwner, languageId, order.StoreId), true, order, order.Customer);
		}

		/// <summary>
		/// Sends an order placed notification to a customer
		/// </summary>
		public static CreateMessageResult SendOrderPlacedCustomerNotification(this IMessageFactory factory, Order order, int languageId = 0)
		{
			Guard.NotNull(order, nameof(order));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.OrderPlacedCustomer, languageId, order.StoreId), true, order, order.Customer);
		}

		/// <summary>
		/// Sends a shipment sent notification to a customer
		/// </summary>
		public static CreateMessageResult SendShipmentSentCustomerNotification(this IMessageFactory factory, Shipment shipment, int languageId = 0)
		{
			Guard.NotNull(shipment, nameof(shipment));
			Guard.NotNull(shipment.Order, nameof(shipment.Order));

			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.ShipmentSentCustomer, languageId, shipment.Order.StoreId), true, shipment, shipment.Order, shipment.Order.Customer);
		}

		/// <summary>
		/// Sends a shipment delivered notification to a customer
		/// </summary>
		public static CreateMessageResult SendShipmentDeliveredCustomerNotification(this IMessageFactory factory, Shipment shipment, int languageId = 0)
		{
			Guard.NotNull(shipment, nameof(shipment));
			Guard.NotNull(shipment.Order, nameof(shipment.Order));

			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.ShipmentDeliveredCustomer, languageId, shipment.Order.StoreId), true, shipment, shipment.Order, shipment.Order.Customer);
		}

		/// <summary>
		/// Sends an order completed notification to a customer
		/// </summary>
		public static CreateMessageResult SendOrderCompletedCustomerNotification(this IMessageFactory factory, Order order, int languageId = 0)
		{
			Guard.NotNull(order, nameof(order));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.OrderCompletedCustomer, languageId, order.StoreId), true, order, order.Customer);
		}

		/// <summary>
		/// Sends an order cancelled notification to a customer
		/// </summary>
		public static CreateMessageResult SendOrderCancelledCustomerNotification(this IMessageFactory factory, Order order, int languageId = 0)
		{
			Guard.NotNull(order, nameof(order));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.OrderCancelledCustomer, languageId, order.StoreId), true, order, order.Customer);
		}

		/// <summary>
		/// Sends a new order note added notification to a customer
		/// </summary>
		public static CreateMessageResult SendNewOrderNoteAddedCustomerNotification(this IMessageFactory factory, OrderNote orderNote, int languageId = 0)
		{
			Guard.NotNull(orderNote, nameof(orderNote));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.OrderNoteAddedCustomer, languageId, orderNote.Order?.StoreId), true, orderNote, orderNote.Order, orderNote.Order.Customer);
		}

		/// <summary>
		/// Sends a "Recurring payment cancelled" notification to a store owner
		/// </summary>
		public static CreateMessageResult SendRecurringPaymentCancelledStoreOwnerNotification(this IMessageFactory factory, RecurringPayment recurringPayment, int languageId = 0)
		{
			Guard.NotNull(recurringPayment, nameof(recurringPayment));
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.RecurringPaymentCancelledStoreOwner, languageId, recurringPayment.InitialOrder?.StoreId), true,
				recurringPayment, recurringPayment.InitialOrder, recurringPayment.InitialOrder?.Customer);
		}

		/// <summary>
		/// Sends 'New Return Request' message to a store owner
		/// </summary>
		public static CreateMessageResult SendNewReturnRequestStoreOwnerNotification(this IMessageFactory factory, ReturnRequest returnRequest, OrderItem orderItem, int languageId = 0)
		{
			Guard.NotNull(returnRequest, nameof(returnRequest));
			Guard.NotNull(orderItem, nameof(orderItem));

			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.NewReturnRequestStoreOwner, languageId, orderItem.Order?.StoreId), true, returnRequest, returnRequest.Customer);
		}

		/// <summary>
		/// Sends 'Return Request status changed' message to a customer
		/// </summary>
		public static CreateMessageResult SendReturnRequestStatusChangedCustomerNotification(this IMessageFactory factory, ReturnRequest returnRequest, OrderItem orderItem, int languageId = 0)
		{
			Guard.NotNull(returnRequest, nameof(returnRequest));
			Guard.NotNull(orderItem, nameof(orderItem));

			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.ReturnRequestStatusChangedCustomer, languageId, orderItem.Order?.StoreId), true, returnRequest, returnRequest.Customer);
		}

		/// <summary>
		/// Sends a gift card notification
		/// </summary>
		public static CreateMessageResult SendGiftCardNotification(this IMessageFactory factory, GiftCard giftCard, int languageId = 0)
		{
			Guard.NotNull(giftCard, nameof(giftCard));

			var storeId = giftCard?.PurchasedWithOrderItem?.Order?.StoreId;
			return factory.CreateMessage(MessageContext.Create(MessageTemplateNames.GiftCardCustomer, languageId, storeId), true, giftCard);
		}
	}
}
