using Library_Project.Model;
using Library_Project.Models;
using Library_Project.Services;
using Library_Project.Services.Interfaces;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Library_Project_Tests
{
    public class LibraryServiceTests
    {
        private readonly Mock<IBookRepository> _bookRepo = new();
        private readonly Mock<IMemberService> _member = new();
        private readonly Mock<INotificationService> _notification = new();
        private readonly Mock<IAuditService> _audit = new();
        private readonly Mock<IBorrowRepository> _borrowRepo = new();
        private readonly Mock<IFineService> _fineService = new();

        private LibraryService CreateService()
        {
            return new LibraryService(
                _bookRepo.Object,
                _member.Object,
                _notification.Object,
                _audit.Object,
                _borrowRepo.Object,
                _fineService.Object
            );
        }

        // --- BOOK RULES (R1 - R3) ---

        [Fact]
        public void Requirement01_AddBook_ShouldThrowIfTitleIsTooShort()
        {
            var service = CreateService();
            string shortTitle = "AB";

            var ex = Assert.Throws<ArgumentException>(() => service.AddBook(shortTitle, 5));
            Assert.Contains("3 characters", ex.Message);
        }

        [Fact]
        public void Requirement02_AddBook_ShouldThrowIfCopiesExceed100()
        {
            var service = CreateService();

            var ex = Assert.Throws<ArgumentException>(() => service.AddBook("Valid Title", 101));
            Assert.Contains("between 1 and 100", ex.Message);
        }

        [Fact]
        public void Requirement03_AddBook_ShouldThrowIfTotalLibraryCapacityExceeds500()
        {
            var service = CreateService();

            _bookRepo.Setup(r => r.GetAllBooks())
                .Returns(new List<Book> { new Book { Title = "Existing", Copies = 495 } });

            var ex = Assert.Throws<InvalidOperationException>(() => service.AddBook("New Book", 10));
            Assert.Equal("Library capacity exceeded.", ex.Message);
        }

        // --- BORROWING RULES & MEMBER RULES (R4, R5, R7-R9, R11-R13) ---

        [Fact]
        public void Requirement04_BorrowBook_ShouldThrowIfBookIsReferenceOnly()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Dictionary";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1, IsReferenceOnly = true });

            var ex = Assert.Throws<InvalidOperationException>(() => service.BorrowBook(memberId, title));
            Assert.Equal("Reference books cannot be borrowed.", ex.Message);
        }

        [Fact]
        public void Requirement05_BorrowBook_ShouldSetDueDateTo14DaysFromNow()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Novel";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 5 });
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            service.BorrowBook(memberId, title);

            _borrowRepo.Verify(r => r.SaveBorrow(It.Is<BorrowRecord>(b =>
                (b.DueDate - b.BorrowDate).TotalDays >= 13.9 && (b.DueDate - b.BorrowDate).TotalDays <= 14.1
            )), Times.Once);
        }

        [Fact]
        public void Requirement07_BorrowBook_ShouldThrowIfMemberHas5ActiveBorrows()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "New Book";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });

            var activeBorrows = Enumerable.Repeat(new BorrowRecord(), 5).ToList();
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(activeBorrows);

            var ex = Assert.Throws<InvalidOperationException>(() => service.BorrowBook(memberId, title));
            Assert.Equal("Borrow limit exceeded.", ex.Message);
        }

        [Fact]
        public void Requirement08_BorrowBook_ShouldThrowIfMemberHasOverdueBooks()
        {
            var service = CreateService();
            int memberId = 1;

            _member.Setup(m => m.GetMember(memberId))
                .Returns(new Member { Status = "Active", HasOverdueBooks = true });

            var ex = Assert.Throws<InvalidOperationException>(() => service.BorrowBook(memberId, "Any Book"));
            Assert.Equal("Member has overdue books.", ex.Message);
        }

        [Fact]
        public void Requirement09_BorrowBook_ShouldThrowIfMemberIsSuspended()
        {
            var service = CreateService();
            int memberId = 1;

            _member.Setup(m => m.GetMember(memberId))
                .Returns(new Member { Status = "Suspended" });

            var ex = Assert.Throws<InvalidOperationException>(() => service.BorrowBook(memberId, "Any Book"));
            Assert.Equal("Member cannot borrow.", ex.Message);
        }

        [Fact]
        public void Requirement11_BorrowBook_ShouldCreateAndSaveBorrowRecord()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Valid Book";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            service.BorrowBook(memberId, title);

            _borrowRepo.Verify(r => r.SaveBorrow(It.IsAny<BorrowRecord>()), Times.Once);
        }

        [Fact]
        public void Requirement12_BorrowBook_ShouldThrowIfBorrowingSameBookTwice()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Same Book";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });

            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId))
                .Returns(new List<BorrowRecord> { new BorrowRecord { Title = title } });

            var ex = Assert.Throws<InvalidOperationException>(() => service.BorrowBook(memberId, title));
            Assert.Equal("Cannot borrow the same book twice.", ex.Message);
        }

        [Fact]
        public void Requirement13_BorrowBook_ShouldLogToAuditService()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Audited Book";

            _member.Setup(m => m.GetMember(memberId)).Returns(new Member { Status = "Active" });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            service.BorrowBook(memberId, title);

            _audit.Verify(a => a.LogBorrow(memberId, title), Times.Once);
        }

        // --- RETURN RULES (R6, R10, R14, R15) ---

        [Fact]
        public void Requirement06_ReturnBook_ShouldSetIsLateTrue_IfReturnedAfterDueDate()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Late Book";
            var pastDate = DateTime.Now.AddDays(-5);

            var record = new BorrowRecord { MemberId = memberId, Title = title, DueDate = pastDate };

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title)).Returns(record);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 0 });

            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());
            
            service.ReturnBook(memberId, title, true);

            Assert.True(record.IsLate);
            _borrowRepo.Verify(r => r.UpdateBorrow(record), Times.Once);
        }

        [Fact]
        public void Requirement10_ReturnBook_ShouldThrowIfSignatureNotConfirmed()
        {
            var service = CreateService();

            var ex = Assert.Throws<InvalidOperationException>(() => service.ReturnBook(1, "Book", false));
            Assert.Equal("Return must be confirmed with signature.", ex.Message);
        }

        [Fact]
        public void Requirement14_ReturnBook_ShouldNotifyAllReturned_WhenNoActiveBorrowsRemain()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Last Book";

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title))
                .Returns(new BorrowRecord { MemberId = memberId, Title = title, DueDate = DateTime.Now.AddDays(1) });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title });

            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            service.ReturnBook(memberId, title, true);

            _notification.Verify(n => n.NotifyAllReturned(memberId), Times.Once);
        }

        [Fact]
        public void Requirement15_ReturnBook_ShouldApplyFine_IfReturnedLate()
        {
            var service = CreateService();
            int memberId = 1;
            string title = "Fined Book";
            var pastDueDate = DateTime.Now.AddDays(-1);

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title))
                .Returns(new BorrowRecord { MemberId = memberId, Title = title, DueDate = pastDueDate });
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title });

            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());
            
            service.ReturnBook(memberId, title, true);

            _fineService.Verify(f => f.ApplyFine(memberId, title), Times.Once);
        }
    }
}
