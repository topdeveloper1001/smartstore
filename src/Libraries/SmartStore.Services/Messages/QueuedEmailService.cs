﻿using System;
using System.Collections.Generic;
using System.Linq;
using SmartStore.Core;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Messages;
using SmartStore.Core.Email;
using SmartStore.Core.Events;
using SmartStore.Core.Logging;
using SmartStore.Services.Localization;

namespace SmartStore.Services.Messages
{
    public partial class QueuedEmailService : IQueuedEmailService
    {
        private readonly IRepository<QueuedEmail> _queuedEmailRepository;
        private readonly IEventPublisher _eventPublisher;
		private readonly IEmailSender _emailSender;
		private readonly ILogger _logger;
		private readonly ILocalizationService _localizationService;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="queuedEmailRepository">Queued email repository</param>
        /// <param name="eventPublisher">Event published</param>
        public QueuedEmailService(
			IRepository<QueuedEmail> queuedEmailRepository,
			IEventPublisher eventPublisher,
			IEmailSender emailSender,
			ILogger logger,
			ILocalizationService localizationService)
        {
            _queuedEmailRepository = queuedEmailRepository;
            _eventPublisher = eventPublisher;
			_emailSender = emailSender;
			_logger = logger;
			_localizationService = localizationService;
        }

        /// <summary>
        /// Inserts a queued email
        /// </summary>
        /// <param name="queuedEmail">Queued email</param>        
        public virtual void InsertQueuedEmail(QueuedEmail queuedEmail)
        {
            if (queuedEmail == null)
                throw new ArgumentNullException("queuedEmail");

            _queuedEmailRepository.Insert(queuedEmail);

            //event notification
            _eventPublisher.EntityInserted(queuedEmail);
        }

        /// <summary>
        /// Updates a queued email
        /// </summary>
        /// <param name="queuedEmail">Queued email</param>
        public virtual void UpdateQueuedEmail(QueuedEmail queuedEmail)
        {
            if (queuedEmail == null)
                throw new ArgumentNullException("queuedEmail");

            _queuedEmailRepository.Update(queuedEmail);

            //event notification
            _eventPublisher.EntityUpdated(queuedEmail);
        }

        /// <summary>
        /// Deleted a queued email
        /// </summary>
        /// <param name="queuedEmail">Queued email</param>
        public virtual void DeleteQueuedEmail(QueuedEmail queuedEmail)
        {
            if (queuedEmail == null)
                throw new ArgumentNullException("queuedEmail");

            _queuedEmailRepository.Delete(queuedEmail);

            //event notification
            _eventPublisher.EntityDeleted(queuedEmail);
        }

        /// <summary>
        /// Gets a queued email by identifier
        /// </summary>
        /// <param name="queuedEmailId">Queued email identifier</param>
        /// <returns>Queued email</returns>
        public virtual QueuedEmail GetQueuedEmailById(int queuedEmailId)
        {
            if (queuedEmailId == 0)
                return null;

            var queuedEmail = _queuedEmailRepository.GetById(queuedEmailId);
            return queuedEmail;

        }

        /// <summary>
        /// Get queued emails by identifiers
        /// </summary>
        /// <param name="queuedEmailIds">queued email identifiers</param>
        /// <returns>Queued emails</returns>
        public virtual IList<QueuedEmail> GetQueuedEmailsByIds(int[] queuedEmailIds)
        {
            if (queuedEmailIds == null || queuedEmailIds.Length == 0)
                return new List<QueuedEmail>();

            var query = from qe in _queuedEmailRepository.Table.Expand(x => x.EmailAccount)
                        where queuedEmailIds.Contains(qe.Id)
                        select qe;

            var queuedEmails = query.ToList();

            // sort by passed identifiers
            var sortedQueuedEmails = new List<QueuedEmail>();

            foreach (int id in queuedEmailIds)
            {
                var queuedEmail = queuedEmails.Find(x => x.Id == id);
                if (queuedEmail != null)
                    sortedQueuedEmails.Add(queuedEmail);
            }
            return sortedQueuedEmails;
        }

        /// <summary>
        /// Gets all queued emails
        /// </summary>
        /// <param name="fromEmail">From Email</param>
        /// <param name="toEmail">To Email</param>
        /// <param name="startTime">The start time</param>
        /// <param name="endTime">The end time</param>
        /// <param name="loadUnsentItemsOnly">A value indicating whether to load only not sent emails</param>
        /// <param name="maxSendTries">Maximum send tries</param>
        /// <param name="loadNewest">A value indicating whether we should sort queued email descending; otherwise, ascending.</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
		/// <param name="sendManually">A value indicating whether to load manually send emails</param>
        /// <returns>Email item list</returns>
        public virtual IPagedList<QueuedEmail> SearchEmails(string fromEmail, 
            string toEmail, DateTime? startTime, DateTime? endTime, 
            bool loadUnsentItemsOnly, int maxSendTries,
            bool loadNewest, int pageIndex, int pageSize,
			bool? sendManually = null)
        {
            fromEmail = (fromEmail ?? String.Empty).Trim();
            toEmail = (toEmail ?? String.Empty).Trim();
            
            var query = _queuedEmailRepository.Table.Expand(x => x.EmailAccount);

            if (!String.IsNullOrEmpty(fromEmail))
                query = query.Where(qe => qe.From.Contains(fromEmail));

            if (!String.IsNullOrEmpty(toEmail))
                query = query.Where(qe => qe.To.Contains(toEmail));

            if (startTime.HasValue)
                query = query.Where(qe => qe.CreatedOnUtc >= startTime);

            if (endTime.HasValue)
                query = query.Where(qe => qe.CreatedOnUtc <= endTime);

            if (loadUnsentItemsOnly)
                query = query.Where(qe => !qe.SentOnUtc.HasValue);

			if (sendManually.HasValue)
				query = query.Where(qe => qe.SendManually == sendManually.Value);

            query = query.Where(qe => qe.SentTries < maxSendTries);
            
			query = query.OrderByDescending(qe => qe.Priority);

            query = loadNewest ? 
                ((IOrderedQueryable<QueuedEmail>)query).ThenByDescending(qe => qe.CreatedOnUtc) :
                ((IOrderedQueryable<QueuedEmail>)query).ThenBy(qe => qe.CreatedOnUtc);

            var queuedEmails = new PagedList<QueuedEmail>(query, pageIndex, pageSize);
            return queuedEmails;
        }

		/// <summary>
		/// Sends a queued email
		/// </summary>
		/// <param name="queuedEmail">Queued email</param>
		/// <returns>Whether the operation succeeded</returns>
		public virtual bool SendEmail(QueuedEmail queuedEmail)
		{
			var result = false;

			try
			{
				var bcc = String.IsNullOrWhiteSpace(queuedEmail.Bcc) ? null : queuedEmail.Bcc.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
				var cc = String.IsNullOrWhiteSpace(queuedEmail.CC) ? null : queuedEmail.CC.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

				var smtpContext = new SmtpContext(queuedEmail.EmailAccount);

				var msg = new EmailMessage(
					new EmailAddress(queuedEmail.To, queuedEmail.ToName),
					queuedEmail.Subject,
					queuedEmail.Body,
					new EmailAddress(queuedEmail.From, queuedEmail.FromName));

				if (queuedEmail.ReplyTo.HasValue())
				{
					msg.ReplyTo.Add(new EmailAddress(queuedEmail.ReplyTo, queuedEmail.ReplyToName));
				}

				if (cc != null)
				{
					msg.Cc.AddRange(cc.Where(x => x.HasValue()).Select(x => new EmailAddress(x)));
				}

				if (bcc != null)
				{
					msg.Bcc.AddRange(bcc.Where(x => x.HasValue()).Select(x => new EmailAddress(x)));
				}

				_emailSender.SendEmail(smtpContext, msg);

				queuedEmail.SentOnUtc = DateTime.UtcNow;
				result = true;
			}
			catch (Exception exc)
			{
				_logger.Error(string.Concat(_localizationService.GetResource("Admin.Common.ErrorSendingEmail"), ": ", exc.Message), exc);
			}
			finally
			{
				queuedEmail.SentTries = queuedEmail.SentTries + 1;
				UpdateQueuedEmail(queuedEmail);
			}
			return result;
		}
    }
}
